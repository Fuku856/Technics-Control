using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly byte[] AutoPowerOffMinutes = [0, 5, 10, 30, 60];

    private readonly DispatcherTimer _ncAdjustTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;
    private NoiseCancelingAdjust? _ncAdjust;
    private byte _autoPowerOffMinutes = 10;

    private DeviceService Device => App.Device;

    public SettingsPage()
    {
        InitializeComponent();
        ShowAppInfo();
        // 狭いときは、横に並べたチェックボックスを縦にする
        ResponsiveHelper.Attach(Scroller, RootPanel,
            width => OutsideTogglePanel.Orientation = width < 600 ? Orientation.Vertical : Orientation.Horizontal);
        // スライダーを動かし終えてから送る
        _ncAdjustTimer.Tick += async (_, _) =>
        {
            _ncAdjustTimer.Stop();
            await CommitNcAdjustAsync((byte)(32 + (int)NcAdjustSlider.Value));
        };
    }

    /// <summary>設定の最下部に出す、アプリ名とバージョン（リリース時は -p:Version で入る）。</summary>
    private void ShowAppInfo()
    {
        var assembly = typeof(SettingsPage).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString(3)
                      ?? "";
        // 「1.2.3+コミットハッシュ」の後ろは出さない
        var plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus];
        AppNameText.Text = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Technics Control";
        VersionText.Text = $"バージョン {version}";
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Device.PropertyChanged += OnDeviceChanged;
        LoadAppSettings();
        await LoadDeviceSettingsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Device.PropertyChanged -= OnDeviceChanged;

    private async void OnDeviceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceService.State))
            await LoadDeviceSettingsAsync();
        else
            StatusView.Apply(StatusBar, RetryButton, Device);
    }

    private void LoadAppSettings()
    {
        _loading = true;
        ThemeBox.SelectedIndex = (int)App.Theme.Theme;
        BodyColorBox.SelectedIndex = (int)App.Settings.BodyColor;
        StartupSwitch.IsOn = StartupService.IsEnabled;
        DisconnectSwitch.IsOn = App.Settings.DisconnectWhenHidden;
        _loading = false;
    }

    /// <summary>イヤホンから今の設定を読み込んで表示する。</summary>
    private async Task LoadDeviceSettingsAsync()
    {
        StatusView.Apply(StatusBar, RetryButton, Device);
        DevicePanel.IsEnabled = ProductInfoCard.IsEnabled = Device.IsConnected;
        if (!Device.IsConnected)
            return;

        await Device.RunAsync(async d =>
        {
            // 公式アプリと同じく、機種 ID の機能ビットで項目の表示を決める（AZ80 は LED の点滅なし）
            var caps = await d.GetCapabilitiesAsync();
            var multiPoint = await d.GetMultiPointAsync();
            var wearing = await d.GetWearingDetectionAsync();
            var autoPowerOff = await d.GetAutoPowerOffAsync();
            var led = caps.LedFlash && await d.GetLedFlashAsync();
            var a2dp = await d.GetA2dpOptionAsync();
            var ncAdjust = await d.GetNoiseCancelingAdjustAsync();
            var ambient = await d.GetAmbientSettingAsync();
            var toggle = await d.GetOutsideToggleAsync();
            var noiseReduction = await d.GetNoiseReductionAsync();

            _loading = true;
            try
            {
                LedCard.Visibility = caps.LedFlash ? Visibility.Visible : Visibility.Collapsed;
                KeyCustomizeCard.Visibility = caps.KeyCustomize || caps.KeyEnable ? Visibility.Visible : Visibility.Collapsed;
                OutsideToggleExpander.Visibility = caps.OutsideToggle ? Visibility.Visible : Visibility.Collapsed;
                MultiPointBox.SelectedIndex = (int)multiPoint;
                ShowWearing(wearing);
                _autoPowerOffMinutes = autoPowerOff.Minutes;
                AutoPowerOffBox.SelectedIndex = autoPowerOff.Enabled ? Math.Max(0, Array.IndexOf(AutoPowerOffMinutes, autoPowerOff.Minutes)) : 0;
                LedSwitch.IsOn = led;
                ConnectionModeBox.SelectedIndex = a2dp.HasFlag(A2dpOption.SoundQualityPriority) ? 0 : 1;
                LdacSwitch.IsOn = a2dp.HasFlag(A2dpOption.Ldac);
                ShowNcAdjust(ncAdjust);
                AmbientTypeBox.SelectedIndex = (int)ambient.Type;
                AmbientStopMusicSwitch.IsOn = ambient.StopMusic;
                ToggleNcCheck.IsChecked = toggle.HasFlag(OutsideToggle.NoiseCanceling);
                ToggleAmbientCheck.IsChecked = toggle.HasFlag(OutsideToggle.Ambient);
                ToggleOffCheck.IsChecked = toggle.HasFlag(OutsideToggle.Off);
                NoiseReductionBox.SelectedIndex = (int)noiseReduction;
            }
            finally
            {
                _loading = false;
            }
            return true;
        });
    }

    /// <summary>設定を送り、失敗したら表示を読み直す。</summary>
    private async Task ApplyAsync(string name, Func<TechnicsDevice, Task> apply)
    {
        if (_loading)
            return;
        ErrorBar.IsOpen = false;
        var ok = await Device.RunAsync(async d =>
        {
            await apply(d);
            return true;
        });
        if (!ok)
        {
            ErrorBar.Title = $"{name}を変更できませんでした";
            ErrorBar.Message = Device.StatusMessage;
            ErrorBar.IsOpen = true;
            await LoadDeviceSettingsAsync();
        }
    }

    private void ShowWearing(WearingDetection value)
    {
        WearingSwitch.IsOn = value.Enabled;
        WearingPauseSwitch.IsOn = value.PauseMusic;
        WearingResumeSwitch.IsOn = value.ResumeMusic;
        WearingTouchSwitch.IsOn = value.TouchWhenNotWorn;
        WearingPauseSwitch.IsEnabled = WearingResumeSwitch.IsEnabled = WearingTouchSwitch.IsEnabled = value.Enabled;
    }

    private void ShowNcAdjust(NoiseCancelingAdjust value)
    {
        _ncAdjust = value;
        NcAdjustSlider.Value = value.Level == NoiseCancelingAdjust.Default ? 0 : value.Level - 32;
        NcAdjustText.Text = NcAdjustLabel(value);
        NcAdjustResetButton.IsEnabled = value.Level != NoiseCancelingAdjust.Default;
    }

    private static string NcAdjustLabel(NoiseCancelingAdjust value) =>
        value.Decibel is { } db ? $"{db:+0.0;-0.0;0.0} dB" : "標準";

    private async void OnMultiPointChanged(object sender, SelectionChangedEventArgs e) =>
        await ApplyAsync("マルチポイントの接続台数", d => d.SetMultiPointAsync((MultiPointMode)MultiPointBox.SelectedIndex));

    private async void OnConnectedExpanded(object sender, EventArgs e)
    {
        ConnectedDevicesText.Text = "読み込んでいます…";
        var names = await Device.RunAsync(d => d.GetConnectedDevicesAsync());
        ConnectedDevicesText.Text = names is null ? "読み込めませんでした" : names.Count == 0 ? "なし" : string.Join("\n", names);
    }

    private void OnKeyCustomizeClick(object sender, RoutedEventArgs e) => App.Instance.MainWindow?.NavigateSub(typeof(KeyCustomizePage));

    private async void OnWearingChanged(object sender, RoutedEventArgs e)
    {
        var value = new WearingDetection(WearingSwitch.IsOn, WearingPauseSwitch.IsOn, WearingTouchSwitch.IsOn, WearingResumeSwitch.IsOn);
        WearingPauseSwitch.IsEnabled = WearingResumeSwitch.IsEnabled = WearingTouchSwitch.IsEnabled = value.Enabled;
        await ApplyAsync("ヘッドホン装着検知", d => d.SetWearingDetectionAsync(value));
    }

    private async void OnAutoPowerOffChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = AutoPowerOffBox.SelectedIndex;
        // OFF のときは、分の値は今の値のまま送る
        var value = index <= 0
            ? new AutoPowerOff(false, AutoPowerOff.AllowedMinutes.Contains(_autoPowerOffMinutes) ? _autoPowerOffMinutes : (byte)10)
            : new AutoPowerOff(true, AutoPowerOffMinutes[index]);
        await ApplyAsync("オートパワーオフ", d => d.SetAutoPowerOffAsync(value));
    }

    private async void OnLedChanged(object sender, RoutedEventArgs e) =>
        await ApplyAsync("LED の点滅", d => d.SetLedFlashAsync(LedSwitch.IsOn));

    private async void OnA2dpChanged(object sender, RoutedEventArgs e)
    {
        var value = (ConnectionModeBox.SelectedIndex == 0 ? A2dpOption.SoundQualityPriority : A2dpOption.ConnectionPriority)
                    | (LdacSwitch.IsOn ? A2dpOption.Ldac : 0);
        await ApplyAsync("接続モード", d => d.SetA2dpOptionAsync(value));
    }

    private void OnNcAdjustSliding(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
            return;
        NcAdjustText.Text = NcAdjustLabel(new NoiseCancelingAdjust((byte)(32 + (int)e.NewValue)));
        _ncAdjustTimer.Stop();
        _ncAdjustTimer.Start();
    }

    private async void OnNcAdjustReset(object sender, RoutedEventArgs e) => await CommitNcAdjustAsync(NoiseCancelingAdjust.Default);

    private async Task CommitNcAdjustAsync(byte level)
    {
        if (_loading || _ncAdjust?.Level == level)
            return;
        var result = await Device.RunAsync(d => d.SetNoiseCancelingAdjustAsync(new NoiseCancelingAdjust(level)));
        if (result is null)
        {
            await LoadDeviceSettingsAsync();
            return;
        }
        _loading = true;
        ShowNcAdjust(result.Value.Agent);
        _loading = false;
        ShowSyncError(result.Value.Matches);
    }

    private async void OnAmbientChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || AmbientTypeBox.SelectedIndex < 0)
            return;
        var value = new AmbientSetting((AmbientType)AmbientTypeBox.SelectedIndex, AmbientStopMusicSwitch.IsOn);
        var result = await Device.RunAsync(d => d.SetAmbientSettingAsync(value));
        if (result is null)
        {
            await LoadDeviceSettingsAsync();
            return;
        }
        ShowSyncError(result.Value.Matches);
        // ホームやトレイに出すモード名（トランスペアレント／アテンション）も変わる
        await Device.RefreshAsync();
    }

    private async void OnOutsideToggleChanged(object sender, RoutedEventArgs e)
    {
        var value = (ToggleNcCheck.IsChecked == true ? OutsideToggle.NoiseCanceling : 0)
                    | (ToggleAmbientCheck.IsChecked == true ? OutsideToggle.Ambient : 0)
                    | (ToggleOffCheck.IsChecked == true ? OutsideToggle.Off : 0);
        if (System.Numerics.BitOperations.PopCount((uint)value) < 2)
        {
            ErrorBar.Title = "2 つ以上選んでください";
            ErrorBar.Message = "タッチ操作で切り替えるモードは 2 つ以上必要です。";
            ErrorBar.IsOpen = true;
            await LoadDeviceSettingsAsync();
            return;
        }
        var result = await Device.RunAsync(d => d.SetOutsideToggleAsync(value));
        if (result is null)
            await LoadDeviceSettingsAsync();
        else
            ShowSyncError(result.Value.Matches);
    }

    private void ShowSyncError(bool matches)
    {
        ErrorBar.IsOpen = !matches;
        ErrorBar.Title = "左右の設定が一致していません";
        ErrorBar.Message = "もう一度変更してください。直らない場合は、イヤホンをケースに戻してから取り出してください。";
    }

    private async void OnNoiseReductionChanged(object sender, SelectionChangedEventArgs e) =>
        await ApplyAsync("通話相手からのノイズを抑える強さ", d => d.SetNoiseReductionAsync((NoiseReduction)NoiseReductionBox.SelectedIndex));

    private void OnBodyColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BodyColorBox.SelectedIndex < 0)
            return;
        App.Settings.BodyColor = (BodyColor)BodyColorBox.SelectedIndex;
        App.Settings.Save();
    }

    private void OnProductInfoClick(object sender, RoutedEventArgs e) => App.Instance.MainWindow?.NavigateSub(typeof(ProductInfoPage));

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeBox.SelectedIndex >= 0)
            App.Theme.Theme = (AppTheme)ThemeBox.SelectedIndex;
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            StartupService.IsEnabled = StartupSwitch.IsOn;
    }

    private void OnDisconnectChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Settings.DisconnectWhenHidden = DisconnectSwitch.IsOn;
        App.Settings.Save();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => Device.Reconnect();
}
