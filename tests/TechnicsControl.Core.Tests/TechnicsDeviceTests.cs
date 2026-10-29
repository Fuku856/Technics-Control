using TechnicsControl.Core.Race;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.Core.Tests;

public class TechnicsDeviceTests
{
    private static async Task<(FakeEarbuds Earbuds, TechnicsDevice Device)> ConnectAsync(Action<FakeEarbuds>? setup = null)
    {
        var earbuds = new FakeEarbuds();
        setup?.Invoke(earbuds);
        var device = await TechnicsDevice.CreateAsync(earbuds);
        device.SyncRetryDelay = TimeSpan.FromMilliseconds(10);
        return (earbuds, device);
    }

    [Fact]
    public async Task Finds_partner_from_available_destinations()
    {
        var (_, device) = await ConnectAsync();
        await using var _ = device;

        Assert.True(device.HasPartner);
    }

    [Fact]
    public async Task Reads_outside_control_from_both_earbuds()
    {
        var (_, device) = await ConnectAsync();
        await using var _ = device;

        var both = await device.ReadBothAsync(role => device.GetOutsideControlAsync(role));

        Assert.Equal(new OutsideControl(OutsideMode.Ambient, 100, 100), both.Agent);
        Assert.True(both.Matches);
    }

    [Fact]
    public async Task Set_outside_control_is_confirmed_on_both_sides()
    {
        var (earbuds, device) = await ConnectAsync();
        await using var _ = device;

        var result = await device.SetOutsideControlAsync(new OutsideControl(OutsideMode.NoiseCanceling, 50, 100));

        Assert.True(result.Value.Matches);
        Assert.Equal(1, result.Attempts);
        Assert.Equal([0x01, 50, 100], earbuds.PartnerOutside);
        // 公式アプリと同じく、主側に 1 回だけ送る
        Assert.Single(earbuds.Sent, p => p.Id == TechnicsCommands.SetOutsideControl);
    }

    [Fact]
    public async Task Resends_to_agent_when_partner_did_not_follow()
    {
        var (earbuds, device) = await ConnectAsync(e => e.PartnerSyncFailures = 1);
        await using var _ = device;

        var result = await device.SetAmbientSettingAsync(new AmbientSetting(AmbientType.Attention, StopMusic: false));

        Assert.True(result.Value.Matches);
        Assert.Equal(2, result.Attempts);
        Assert.False(result.UsedPartnerRelay);
        Assert.Equal([0x01, 0x00], earbuds.PartnerAmbient);
    }

    [Fact]
    public async Task Reports_mismatch_without_relaying_unless_allowed()
    {
        var (earbuds, device) = await ConnectAsync(e => e.PartnerSyncFailures = 5);
        await using var _ = device;

        var result = await device.SetOutsideControlAsync(new OutsideControl(OutsideMode.Off, 100, 100));

        Assert.False(result.Value.Matches);
        Assert.False(result.UsedPartnerRelay);
        Assert.DoesNotContain(earbuds.Sent, p => p.Id == RelayIds.PassToDst && RacePacket.Parse(p.Payload.AsSpan(2)).Id == TechnicsCommands.SetOutsideControl);
    }

    [Fact]
    public async Task Relays_set_to_partner_when_allowed()
    {
        var (earbuds, device) = await ConnectAsync(e => e.PartnerSyncFailures = 5);
        await using var _ = device;
        device.AllowPartnerRelaySet = true;

        var result = await device.SetOutsideControlAsync(new OutsideControl(OutsideMode.Off, 100, 100));

        Assert.True(result.Value.Matches);
        Assert.True(result.UsedPartnerRelay);
        Assert.Equal([0x00, 100, 100], earbuds.PartnerOutside);
    }

    [Fact]
    public async Task Single_earbud_is_not_compared()
    {
        var (_, device) = await ConnectAsync(e => e.HasPartner = false);
        await using var _ = device;

        var result = await device.SetOutsideControlAsync(new OutsideControl(OutsideMode.NoiseCanceling, 1, 100));

        Assert.False(result.Value.PartnerPresent);
        Assert.True(result.Value.Matches);
    }

    [Fact]
    public async Task Battery_is_read_per_earbud()
    {
        var (_, device) = await ConnectAsync();
        await using var _ = device;

        Assert.Equal(new BatteryLevel(EarbudRole.Agent, 70), await device.GetBatteryAsync(EarbudRole.Agent));
        Assert.Equal(new BatteryLevel(EarbudRole.Partner, 72), await device.GetBatteryAsync(EarbudRole.Partner));
    }

    [Fact]
    public async Task Touch_operation_on_earbud_raises_change_event()
    {
        var (earbuds, device) = await ConnectAsync();
        await using var _ = device;
        var changed = new TaskCompletionSource<OutsideControl>();
        device.OutsideControlChanged += v => changed.TrySetResult(v);

        earbuds.Push(new RacePacket(RaceType.Indication, TechnicsCommands.GetOutsideControl, [0x00, 0x01, 0x32, 0x64]));

        Assert.Equal(new OutsideControl(OutsideMode.NoiseCanceling, 50, 100), await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(101, 100)]
    [InlineData(50, 0)]
    public void Levels_outside_1_to_100_are_rejected(byte nc, byte ambient)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutsideControl(OutsideMode.NoiseCanceling, nc, ambient).ToPayload());
    }

    [Fact]
    public void Noise_canceling_adjust_maps_to_decibels()
    {
        Assert.Null(new NoiseCancelingAdjust(0).Decibel);
        Assert.Equal(-6.0, new NoiseCancelingAdjust(20).Decibel);
        Assert.Equal(2.0, new NoiseCancelingAdjust(0x24).Decibel);
        Assert.Equal(4.0, new NoiseCancelingAdjust(40).Decibel);
    }
}
