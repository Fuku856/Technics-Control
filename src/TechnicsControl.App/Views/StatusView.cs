using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TechnicsControl.App.Services;

namespace TechnicsControl.App.Views;

/// <summary>接続状態と左右の一致状態の表示（ホーム、外音コントロール、クイックパネルで共通）。</summary>
internal static class StatusView
{
    public static void Apply(InfoBar bar, Button retry, DeviceService device)
    {
        bar.IsOpen = !device.IsConnected;
        bar.Message = device.StatusMessage;
        bar.Severity = device.State switch
        {
            ConnectionState.Busy or ConnectionState.NotFound or ConnectionState.Paused => InfoBarSeverity.Warning,
            ConnectionState.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
        bar.Title = device.State switch
        {
            ConnectionState.Busy => "他の機器が使用中",
            ConnectionState.NotFound => "イヤホンが見つかりません",
            ConnectionState.Paused => "接続を切っています",
            ConnectionState.Error => "接続できません",
            _ => "接続しています",
        };
        retry.Visibility = device.State == ConnectionState.Searching ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 左右の設定が食い違っているときだけ知らせる。一致しているとき・確認中は何も出さない（片耳だけのときはそれを知らせる）。
    /// </summary>
    public static void ApplySync(FontIcon icon, TextBlock text, DeviceService device)
    {
        // 色は App.xaml のスタイル（ThemeResource）で指定し、要素ごとのテーマに追従させる
        (string Glyph, string Text, Style Style)? status = !device.IsConnected ? null : device.LeftRightMatch switch
        {
            false => ("\uE7BA", "左右の設定が一致していません", Style("SyncWarningIconStyle")),
            true when device.MismatchedSettings.Count > 0 =>
                ("\uE7BA", $"左右で違う設定があります（{string.Join("、", device.MismatchedSettings)}）", Style("SyncWarningIconStyle")),
            null when !device.PartnerPresent => ("\uE946", "片方のイヤホンだけがつながっています", Style("SyncInfoIconStyle")),
            _ => null,
        };
        Show(icon, text, status);
    }

    /// <summary>状態を表示する。null なら隠す。</summary>
    public static void Show(FontIcon icon, TextBlock text, (string Glyph, string Text, Style Style)? status)
    {
        icon.Visibility = text.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        if (status is { } s)
            (icon.Glyph, text.Text, icon.Style) = s;
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];
}
