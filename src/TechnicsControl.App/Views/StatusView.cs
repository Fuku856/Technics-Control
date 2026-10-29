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

    public static void ApplySync(FontIcon icon, TextBlock text, DeviceService device)
    {
        var visible = device.IsConnected;
        icon.Visibility = text.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // 色は App.xaml のスタイル（ThemeResource）で指定し、要素ごとのテーマに追従させる
        (icon.Glyph, text.Text, icon.Style) = device.LeftRightMatch switch
        {
            true when device.MismatchedSettings.Count > 0 =>
                ("\uE7BA", $"左右で違う設定があります（{string.Join("、", device.MismatchedSettings)}）", Style("SyncWarningIconStyle")),
            true => ("\uE73E", "左右の設定は一致しています", Style("SyncOkIconStyle")),
            false => ("\uE7BA", "左右の設定が一致していません", Style("SyncWarningIconStyle")),
            null when device.IsConnected && !device.PartnerPresent => ("\uE946", "片方のイヤホンだけがつながっています", Style("SyncInfoIconStyle")),
            _ => ("\uE946", "左右の設定を確認しています…", Style("SyncInfoIconStyle")),
        };
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];
}
