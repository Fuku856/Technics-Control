using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Views.Pages;

/// <summary>サウンドモード（イコライザーのプリセット）。公式アプリの fragment_sound_mode_zui に当たる。</summary>
public sealed partial class SoundModePage : Page
{
    // 公式アプリの並び順（row_sound_mode）。「ダイレクト」は AZ80 では「オフ」と同じ番号なので出さない
    private static readonly (SoundMode Mode, string Name, string Icon)[] Modes =
    [
        (SoundMode.Off, "オフ", "ic_normal_regular_42px"),
        (SoundMode.BassEnhancer, "バスエンハンサー", "ic_bass_regular_42px"),
        (SoundMode.BassEnhancerPlus, "バスエンハンサー＋", "ic_superbass_regular_42px"),
        (SoundMode.ClearVoice, "クリアボイス", "ic_vocals_regular_42px"),
        (SoundMode.TrebleBoost, "トレブル＋", "ic_treble_42px"),
        (SoundMode.Dynamic, "ダイナミック", "ic_dynamic_42px"),
        (SoundMode.Custom, "カスタム", "ic_custom_regular_42px"),
        (SoundMode.Custom2, "カスタム2", "ic_custom_regular_42px"),
        (SoundMode.Custom3, "カスタム3", "ic_custom_regular_42px"),
    ];

    private bool _loading;
    private SoundMode? _current;

    private DeviceService Device => App.Device;

    public SoundModePage()
    {
        InitializeComponent();
        ResponsiveHelper.Attach(Scroller, RootPanel);
        foreach (var (mode, name, icon) in Modes)
        {
            // 項目はすべて同じ大きさで並ぶので、いちばん長い名前（バスエンハンサー＋）が 2 行で収まる高さにする
            var content = new StackPanel { Width = 128, Height = 108, Spacing = 8, Padding = new Thickness(8) };
            content.Children.Add(new BitmapIcon { UriSource = Assets.Icon(icon), ShowAsMonochrome = true, Width = 36, Height = 36 });
            content.Children.Add(new TextBlock
            {
                Text = name, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var item = new GridViewItem { Content = content, Tag = mode };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, name);
            ModeGrid.Items.Add(item);
        }
    }

    public static string ModeName(SoundMode mode) => Modes.FirstOrDefault(m => m.Mode == mode).Name ?? $"不明（{(byte)mode}）";

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Device.PropertyChanged += OnDeviceChanged;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Device.PropertyChanged -= OnDeviceChanged;

    private async void OnDeviceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceService.State))
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusView.Apply(StatusBar, RetryButton, Device);
        ModeGrid.IsEnabled = Device.IsConnected;
        if (!Device.IsConnected)
            return;
        var both = await Device.RunAsync(d => d.ReadBothAsync(role => d.GetSoundModeAsync(role)));
        if (both is null)
            return;
        Show(both.Agent, both.Matches, both.PartnerPresent);
    }

    private void Show(SoundMode mode, bool matches, bool partnerPresent)
    {
        _loading = true;
        try
        {
            _current = mode;
            ModeGrid.SelectedItem = ModeGrid.Items.OfType<GridViewItem>().FirstOrDefault(i => (SoundMode)i.Tag == mode);
            CurrentText.Text = $"現在: {ModeName(mode)}";
            // 左右が食い違っているときだけ知らせる（一致していれば何も出さない）
            StatusView.Show(SyncIcon, SyncText, (matches, partnerPresent) switch
            {
                (false, _) => ("\uE7BA", "左右の設定が一致していません", (Style)Application.Current.Resources["SyncWarningIconStyle"]),
                (true, false) => ("\uE946", "片方のイヤホンだけがつながっています", (Style)Application.Current.Resources["SyncInfoIconStyle"]),
                _ => null,
            });
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeGrid.SelectedItem is not GridViewItem { Tag: SoundMode mode } || mode == _current)
            return;
        ErrorBar.IsOpen = false;
        ApplyingRing.IsActive = true;
        try
        {
            var result = await Device.RunAsync(d => d.SetSoundModeAsync(mode));
            if (result is null)
            {
                ErrorBar.Title = "サウンドモードを変更できませんでした";
                ErrorBar.Message = Device.StatusMessage;
                ErrorBar.IsOpen = true;
                await LoadAsync();
                return;
            }
            Show(result.Value.Agent, result.Value.Matches, result.Value.PartnerPresent);
        }
        finally
        {
            ApplyingRing.IsActive = false;
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => Device.Reconnect();
}
