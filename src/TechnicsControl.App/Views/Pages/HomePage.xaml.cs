using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;

namespace TechnicsControl.App.Views.Pages;

public sealed partial class HomePage : Page
{
    private DeviceService Device => App.Device;

    public HomePage()
    {
        InitializeComponent();
        ResponsiveHelper.Attach(Scroller, RootPanel);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Device.PropertyChanged += OnDeviceChanged;
        App.Settings.BodyColorChanged += Update;
        Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Device.PropertyChanged -= OnDeviceChanged;
        App.Settings.BodyColorChanged -= Update;
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        DeviceNameText.Text = Device.DeviceName;
        StatusView.Apply(StatusBar, RetryButton, Device);

        var codec = Assets.CodecIcon(Device.IsConnected ? Device.Codec?.Type : null);
        CodecIcon.Visibility = codec is null ? Visibility.Collapsed : Visibility.Visible;
        if (codec is not null && CodecIcon.UriSource != codec)
            CodecIcon.UriSource = codec;
        ToolTipService.SetToolTip(CodecIcon, Device.Codec is { } c ? $"{c.Type} {c.SamplingFrequency / 1000.0:0.#} kHz" : null);

        var color = App.Settings.BodyColor;
        SetImage(LeftImage, Assets.DeviceBitmap(color, 'l'));
        SetImage(RightImage, Assets.DeviceBitmap(color, 'r'));
        SetImage(CaseImage, Assets.DeviceBitmap(color, 'c'));

        // 片耳だけのとき（ケースの中など）は、つながっていない側を薄く表示する
        var connected = Device.IsConnected;
        LeftImage.Opacity = connected && Device.LeftBattery is not null ? 1 : 0.35;
        RightImage.Opacity = connected && Device.RightBattery is not null ? 1 : 0.35;
        LeftBatteryText.Text = BatteryText("L", connected ? Device.LeftBattery : null);
        RightBatteryText.Text = BatteryText("R", connected ? Device.RightBattery : null);
        CaseBatteryText.Text = !connected ? "" : Device.CaseBattery is { } cradle ? $"{cradle}%" : "—";

        ModeCard.IsEnabled = connected;
        ModeText.Text = connected ? Device.ModeText : "—";
        LevelText.Text = connected && Device.Level is { } level ? level.ToString() : "";
        if (Device.Mode is { } mode)
            ModeIcon.UriSource = Assets.ModeIcon(mode);

        StatusView.ApplySync(SyncIcon, SyncText, Device);
    }

    private static void SetImage(Image image, BitmapImage bitmap)
    {
        if (!ReferenceEquals(image.Source, bitmap))
            image.Source = bitmap;
    }

    private static string BatteryText(string side, int? percent) => percent is { } p ? $"{side}  {p}%" : $"{side}  未接続";

    private void OnRetryClick(object sender, RoutedEventArgs e) => Device.Reconnect();

    private void OnModeCardClick(object sender, RoutedEventArgs e) => App.Instance.MainWindow?.Navigate("Outside");
}
