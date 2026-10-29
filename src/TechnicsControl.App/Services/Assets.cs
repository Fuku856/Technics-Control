using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Services;

/// <summary>
/// 公式アプリから取り出した画像とアイコンの場所（Assets/Official、tools/AssetExtractor で作る）。
/// アイコンは白一色の画像を BitmapIcon（ShowAsMonochrome）で文字色に塗って使うので、テーマに自動で合う。
/// </summary>
public static class Assets
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets", "Official");

    public static Uri Icon(string name) => new($"ms-appx:///Assets/Official/Icons/Dark/{name}.png");

    public static Uri DeviceImage(BodyColor color, char part) =>
        new($"ms-appx:///Assets/Official/Device/az80_{ColorCode(color)}_{part}.png");

    public static string TrayIconPath(UiMode? mode, bool lightTaskbar) =>
        Path.Combine(Root, "Tray", $"{mode switch
        {
            UiMode.NoiseCanceling => "nc",
            UiMode.Transparent or UiMode.Attention => "ambient",
            _ => "off",
        }}_{(lightTaskbar ? "light" : "dark")}.ico");

    public static string AppIconPath => Path.Combine(Root, "AppIcon.ico");

    public static Uri ModeIcon(UiMode mode) => Icon(mode switch
    {
        UiMode.NoiseCanceling => "ic_noise_cancellation_regular_96px",
        UiMode.Transparent or UiMode.Attention => "ic_ambiet_sound_regular_96px",
        _ => "ic_normal_regular_96px",
    });

    public static Uri? CodecIcon(CodecType? type) => type switch
    {
        CodecType.Ldac => Icon("icon_codec_ldac"),
        CodecType.Aac => Icon("icon_codec_aac"),
        CodecType.Sbc => Icon("icon_codec_sbc"),
        CodecType.UsbAudio => Icon("icon_codec_usb_audio"),
        CodecType.AnalogAudio => Icon("icon_codec_analog"),
        _ => null,
    };

    private static string ColorCode(BodyColor color) => color switch
    {
        BodyColor.Silver => "slv",
        BodyColor.MidnightBlue => "nvy",
        _ => "blk",
    };
}
