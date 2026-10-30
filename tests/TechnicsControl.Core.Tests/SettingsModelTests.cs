using TechnicsControl.Core.Race;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.Core.Tests;

public class SettingsModelTests
{
    private static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", ""));

    [Fact]
    public void Parses_recorded_left_keymap()
    {
        // 実機（左）の 0x0019 の通知
        var packet = RacePacket.Parse(Hex("05 5D 11 00 19 00 00 00 00 10 23 13 25 30 00 00 31 00 23 13 40"));

        var map = Keymap.FromPayload(packet.Payload.AsSpan(1));

        Assert.Equal(KeySide.Left, map.Side);
        Assert.Equal([KeyFunction.PlayPause, KeyFunction.OutsideSwitching, KeyFunction.VolumeDown, KeyFunction.VoiceAssistant], map.Music);
        Assert.Equal([KeyFunction.AnswerCall, KeyFunction.None, KeyFunction.None, KeyFunction.RejectCall], map.IncomingCall);
        Assert.Equal([KeyFunction.None, KeyFunction.OutsideSwitching, KeyFunction.VolumeDown, KeyFunction.EndCall], map.Talking);
    }

    [Fact]
    public void Keymap_payload_matches_device_format()
    {
        var packet = RacePacket.Parse(Hex("05 5D 11 00 19 00 00 01 00 10 00 14 25 30 00 00 31 00 41 14 40"));
        var map = Keymap.FromPayload(packet.Payload.AsSpan(1));

        Assert.Equal(KeySide.Right, map.Side);
        Assert.Equal(Hex("01 00 10 00 14 25 30 00 00 31 00 41 14 40"), map.ToPayload());
        Assert.Equal(map, Keymap.FromPayload(map.ToPayload()));
    }

    [Fact]
    public void Az80_capabilities_hide_led_setting_like_official_app()
    {
        // 実機（AZ80）の 0x0000 の応答: 05 5B 09 00 00 00 | 00 | 70 00 03 FB 19 02
        var caps = DeviceCapabilities.FromPayload(Hex("70 00 03 FB 19 02"));

        Assert.Equal(0x0070, caps.ModelId);
        Assert.False(caps.LedFlash);
        Assert.True(caps.KeyEnable);
        Assert.True(caps.OutsideToggle);
        Assert.True(caps.KeyCustomize);
        Assert.True(caps.MusicVideoBuffer);
    }

    [Fact]
    public void Parses_recorded_wearing_detection()
    {
        var value = WearingDetection.FromPayload(Hex("01 01 00 01"));

        Assert.Equal(new WearingDetection(Enabled: true, PauseMusic: true, TouchWhenNotWorn: false, ResumeMusic: true), value);
        Assert.Equal(Hex("01 01 00 01"), value.ToPayload());
    }

    [Fact]
    public void A2dp_option_recorded_value_is_ldac_with_sound_quality()
    {
        var value = (A2dpOption)0x11;

        Assert.True(value.HasFlag(A2dpOption.Ldac));
        Assert.True(value.HasFlag(A2dpOption.SoundQualityPriority));
    }

    [Fact]
    public void Auto_power_off_rejects_unsupported_minutes()
    {
        Assert.Equal(Hex("01 0A"), new AutoPowerOff(true, 10).ToPayload());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoPowerOff(true, 15).ToPayload());
    }
}
