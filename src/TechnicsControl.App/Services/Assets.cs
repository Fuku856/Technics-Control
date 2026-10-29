using Microsoft.UI.Xaml.Media.Imaging;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Services;

/// <summary>
/// 公式アプリから取り出した画像とアイコンの場所（Assets/Official、tools/AssetExtractor で作る）。
/// アイコンは白一色の画像を BitmapIcon（ShowAsMonochrome）で文字色に塗って使うので、テーマに自動で合う。
/// </summary>
public static class Assets
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets", "Official");
    private static readonly Dictionary<Uri, BitmapImage> DeviceBitmaps = [];

    public static Uri Icon(string name) => new($"ms-appx:///Assets/Official/Icons/Dark/{name}.png");

    private static Uri DeviceImage(BodyColor color, char part) =>
        new($"ms-appx:///Assets/Official/Device/az80_{ColorCode(color)}_{part}.png");

    /// <summary>本体の画像。一度読み込んだものを使い回す（UI スレッドで使う）。</summary>
    public static BitmapImage DeviceBitmap(BodyColor color, char part)
    {
        var uri = DeviceImage(color, part);
        if (!DeviceBitmaps.TryGetValue(uri, out var bitmap))
        {
            bitmap = new BitmapImage(uri);
            DeviceBitmaps[uri] = bitmap;
        }
        return bitmap;
    }

    /// <summary>
    /// 本体の画像を先に読み込んでおく。Image に初めて表示したときに読み込むと、ウィンドウが出てから画像が遅れて出るため。
    /// </summary>
    public static async Task PreloadDeviceBitmapsAsync(BodyColor color)
    {
        foreach (var part in "lrc")
        {
            var uri = DeviceImage(color, part);
            if (DeviceBitmaps.ContainsKey(uri))
                continue;
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(Path.Combine(Root, "Device", Path.GetFileName(uri.LocalPath))).AsRandomAccessStream())
                    await bitmap.SetSourceAsync(stream);
                DeviceBitmaps[uri] = bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // 読めなければ、表示するときにふつうに読み込む
            }
        }
    }

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
