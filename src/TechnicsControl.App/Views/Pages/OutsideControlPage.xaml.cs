using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;

namespace TechnicsControl.App.Views.Pages;

public sealed partial class OutsideControlPage : Page
{
    private bool _updating;

    private DeviceService Device => App.Device;

    public OutsideControlPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Device.PropertyChanged += OnDeviceChanged;
        Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Device.PropertyChanged -= OnDeviceChanged;

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        _updating = true;
        try
        {
            StatusView.Apply(StatusBar, RetryButton, Device);
            var connected = Device.IsConnected && Device.Mode is not null;
            ModeSelector.IsEnabled = connected;
            // 切り替え中は、選んだモードの表示をそのまま残す（イヤホンの古い値で戻さない）
            if (!Device.IsApplying)
                ModeSelector.SelectedIndex = Device.Mode is { } mode ? (int)mode : -1;
            ModeTitle.Text = connected ? Device.ModeText : "";

            // アテンションと OFF には強さがない（Device.Level が null）
            Dial.IsEnabled = connected && Device.Level is not null;
            Dial.Caption = Device.Mode switch
            {
                UiMode.NoiseCanceling => "ノイズキャンセリングの強さ",
                UiMode.Transparent => "外音取り込みの強さ",
                UiMode.Attention => "アテンションのときは調整できません",
                _ => "OFF のときは調整できません",
            };
            // 操作中（ドラッグ中や、ホイール・キーで動かして確定する前）はイヤホン側の値で上書きしない
            if (!Dial.IsInteracting && Device.Level is { } level)
                Dial.Value = level;

            ApplyingRing.IsActive = Device.IsApplying;
            StatusView.ApplySync(SyncIcon, SyncText, Device);
        }
        finally
        {
            _updating = false;
        }
    }

    private async void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ModeSelector.SelectedItem is not FrameworkElement { Tag: string tag })
            return;
        var mode = Enum.Parse<UiMode>(tag);
        // 表示を今のモードに合わせ直したときにも選択変更が届くので、同じモードなら何もしない
        if (mode == Device.Mode && !Device.IsApplying)
            return;
        await Device.SetModeAsync(mode);
        Update();
    }

    private void OnDialChanging(object? sender, int value) => Device.PreviewLevel(value);

    private async void OnDialCommitted(object? sender, int value) => await Device.CommitLevelAsync(value);

    private void OnRetryClick(object sender, RoutedEventArgs e) => Device.Reconnect();
}
