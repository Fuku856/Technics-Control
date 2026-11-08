using System.Threading.Channels;
using TechnicsControl.Core.Race;
using TechnicsControl.Core.Transport;

namespace TechnicsControl.Core.Tests;

/// <summary>
/// AZ80 の RACE の振る舞いをまねる模擬イヤホン。値は実機で記録したものを初期値にしている。
/// </summary>
internal sealed class FakeEarbuds : IRaceTransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

    public byte[] AgentOutside { get; set; } = [0x02, 0x64, 0x64];
    public byte[] PartnerOutside { get; set; } = [0x02, 0x64, 0x64];
    public byte[] AgentAmbient { get; set; } = [0x00, 0x00];
    public byte[] PartnerAmbient { get; set; } = [0x00, 0x00];
    public bool HasPartner { get; set; } = true;
    public byte CradleBattery { get; set; } = 0x64;

    /// <summary>主側への設定が相方に反映されない回数（左右がずれる状況の再現）。</summary>
    public int PartnerSyncFailures { get; set; }

    public List<RacePacket> Sent { get; } = [];

    /// <summary>相方の外音コントロールを読まれた直後に呼ぶ（相方への反映が遅れる状況の再現）。</summary>
    public Action? AfterPartnerOutsideRead { get; set; }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var request = RacePacket.Parse(data.Span);
        Sent.Add(request);
        foreach (var reply in Handle(request, partner: false))
            _incoming.Writer.TryWrite(reply.ToBytes());
        return Task.CompletedTask;
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
            return 0;
        var chunk = await _incoming.Reader.ReadAsync(cancellationToken);
        chunk.CopyTo(buffer);
        return chunk.Length;
    }

    /// <summary>イヤホン側からの通知を送る（タッチ操作など）。</summary>
    public void Push(RacePacket packet) => _incoming.Writer.TryWrite(packet.ToBytes());

    public void Disconnect() => _incoming.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        _incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private IEnumerable<RacePacket> Handle(RacePacket request, bool partner)
    {
        RacePacket Ok(params byte[] data) => new(RaceType.Response, request.Id, [0x00, .. data]);

        switch (request.Id)
        {
            case RelayIds.GetAvailableDst:
                yield return new RacePacket(RaceType.Response, request.Id, HasPartner ? [0x05, 0x06] : []);
                break;
            case RelayIds.PassToDst:
                yield return Ok();
                var inner = RacePacket.Parse(request.Payload.AsSpan(2));
                foreach (var reply in Handle(inner, partner: true))
                    yield return new RacePacket(RaceType.Indication, RelayIds.PassToDst, [request.Payload[0], request.Payload[1], .. reply.ToBytes()]);
                break;
            case 0x000A:
                yield return Ok(partner ? PartnerOutside : AgentOutside);
                if (partner)
                    AfterPartnerOutsideRead?.Invoke();
                break;
            case 0x000B:
                Apply(request.Payload, partner, v => AgentOutside = v, v => PartnerOutside = v);
                yield return Ok();
                break;
            case 0x0021:
                yield return Ok(partner ? PartnerAmbient : AgentAmbient);
                break;
            case 0x0022:
                Apply(request.Payload, partner, v => AgentAmbient = v, v => PartnerAmbient = v);
                yield return Ok();
                break;
            case 0x0040:
                yield return Ok(CradleBattery);
                break;
            case 0x0CD6:
                yield return Ok();
                yield return new RacePacket(RaceType.Indication, request.Id, [0x00, request.Payload[0], request.Payload[0] == 0 ? (byte)0x46 : (byte)0x48]);
                break;
            default:
                // 非対応のコマンドは実機と同じく応答しない
                break;
        }
    }

    private void Apply(byte[] value, bool partner, Action<byte[]> setAgent, Action<byte[]> setPartner)
    {
        if (partner)
        {
            setPartner(value);
            return;
        }
        setAgent(value);
        if (PartnerSyncFailures > 0)
            PartnerSyncFailures--;
        else if (HasPartner)
            setPartner(value);
    }
}
