using TechnicsControl.Core.Race;

namespace TechnicsControl.Core.Tests;

public class RaceTests
{
    private static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", ""));

    [Fact]
    public void Request_matches_bytes_sent_to_real_device()
    {
        Assert.Equal(Hex("05 5A 02 00 0A 00"), RacePacket.Request(0x000A).ToBytes());
        Assert.Equal(Hex("05 5A 03 00 D6 0C 01"), RacePacket.Request(0x0CD6, 0x01).ToBytes());
    }

    [Fact]
    public void Parses_recorded_outside_control_response()
    {
        var packet = RacePacket.Parse(Hex("05 5B 06 00 0A 00 00 02 64 64"));

        Assert.Equal(RaceType.Response, packet.Type);
        Assert.Equal(0x000A, packet.Id);
        Assert.Equal(0, packet.Status);
        Assert.Equal(0x02, packet.At(7));
        Assert.Equal(0x64, packet.At(9));
    }

    [Fact]
    public void Framer_splits_concatenated_packets_and_skips_garbage()
    {
        var framer = new RaceFramer();
        var first = framer.Push(Hex("FF 00 05 5B 03 00 D6 0C 00 05 5D 05")).ToList();
        var second = framer.Push(Hex("00 D6 0C 00 00 46")).ToList();

        Assert.Single(first);
        Assert.Equal(RaceType.Response, first[0].Type);
        Assert.Single(second);
        Assert.Equal(0x46, second[0].At(8));
    }

    [Fact]
    public void Unwraps_recorded_relay_indication()
    {
        var relay = RacePacket.Parse(Hex("05 5D 0E 00 01 0D 05 06 05 5B 06 00 0A 00 00 02 64 64"));

        var inner = RaceClient.UnwrapRelay(relay);

        Assert.NotNull(inner);
        Assert.Equal(0x000A, inner.Id);
        Assert.Equal(0x02, inner.At(7));
    }

    [Theory]
    [InlineData(0x1C05)] // ソフトウェアリセット
    [InlineData(0x1C08)] // FOTA 開始
    [InlineData(0x0A01)] // NVKey 書き込み
    [InlineData(0x0041)] // 初期化
    [InlineData(0x0042)] // 電源 OFF
    public async Task Whitelist_blocks_dangerous_commands(ushort id)
    {
        await using var earbuds = new FakeEarbuds();
        await using var client = new RaceClient(earbuds);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(RacePacket.Request(id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendToPartnerAsync(0x05, 0x06, RacePacket.Request(id)));
        Assert.Empty(earbuds.Sent);
    }

    [Fact]
    public void Peq_commands_are_allowed_only_for_equalizer_module()
    {
        RaceWhitelist.Ensure(RacePacket.Request(PeqIds.GetGroup, 0, 0));
        RaceWhitelist.Ensure(RacePacket.Request(PeqIds.SetGroup, 0, 0, 9));
        // モジュール 5 は ANC。汎用コマンドで ANC を直接操作しない
        Assert.Throws<InvalidOperationException>(() => RaceWhitelist.Ensure(RacePacket.Request(PeqIds.GetGroup, 5, 0)));
        Assert.Throws<InvalidOperationException>(() => RaceWhitelist.Ensure(RacePacket.Request(PeqIds.SetGroup, 5, 0, 1)));
    }

    [Fact]
    public async Task Waits_for_indication_that_arrives_with_response()
    {
        await using var earbuds = new FakeEarbuds();
        await using var client = new RaceClient(earbuds);

        var reply = await client.SendAsync(RacePacket.Request(0x0CD6, 0x01), RaceReply.Indication);

        Assert.Equal(RaceType.Indication, reply.Type);
        Assert.Equal(0x48, reply.At(8));
    }

    [Fact]
    public async Task Times_out_when_device_does_not_answer()
    {
        await using var earbuds = new FakeEarbuds();
        await using var client = new RaceClient(earbuds) { Timeout = TimeSpan.FromMilliseconds(200) };

        // 0x0067（アダプティブ ANC）は AZ80 では応答しない
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(RacePacket.Request(0x0067)));
    }

    [Fact]
    public async Task Unsolicited_indication_is_raised_as_event()
    {
        await using var earbuds = new FakeEarbuds();
        await using var client = new RaceClient(earbuds);
        var received = new TaskCompletionSource<RacePacket>();
        client.IndicationReceived += p => received.TrySetResult(p);

        earbuds.Push(new RacePacket(RaceType.Indication, 0x000A, [0x00, 0x01, 0x32, 0x64]));

        var packet = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0x01, packet.At(7));
    }

    [Fact]
    public async Task Pending_request_fails_when_connection_drops()
    {
        await using var earbuds = new FakeEarbuds();
        await using var client = new RaceClient(earbuds) { Timeout = TimeSpan.FromSeconds(5) };

        var pending = client.SendAsync(RacePacket.Request(0x0067));
        earbuds.Disconnect();

        await Assert.ThrowsAsync<RaceException>(() => pending);
        Assert.True(client.IsClosed);
    }
}
