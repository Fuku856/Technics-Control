using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TechnicsControl.App.Services;
using Windows.Graphics;

namespace TechnicsControl.App.Views;

/// <summary>トレイアイコンの左クリックで、タスクバーの上に出す小さなパネル。</summary>
public sealed partial class QuickPanelWindow : Window
{
    private const int PanelWidth = 440;
    private const int PanelHeight = 300;

    private readonly DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _updating;

    private DeviceService Device => App.Device;

    public QuickPanelWindow()
    {
        InitializeComponent();
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Assets.AppIconPath);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        // フォーカスが外れたら閉じる（タスクバーのフライアウトと同じ振る舞い）
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                HidePanel();
        };
        _commitTimer.Tick += async (_, _) =>
        {
            _commitTimer.Stop();
            await Device.CommitLevelAsync((int)LevelSlider.Value);
        };
        Device.PropertyChanged += OnDeviceChanged;
        Closed += (_, _) => Device.PropertyChanged -= OnDeviceChanged;
    }

    public bool IsShown { get; private set; }

    public void ShowPanel()
    {
        // カーソルのあるディスプレイの作業領域の右下（タスクバーの上）に出す
        var cursor = NativeCursor.Position();
        var area = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Primary).WorkArea;
        var scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var width = (int)(PanelWidth * scale);
        var height = (int)(PanelHeight * scale);
        var margin = (int)(12 * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - width - margin, area.Y + area.Height - height - margin, width, height));
        Update();
        AppWindow.Show();
        Activate();
        IsShown = true;
        _ = Device.RefreshAsync();
    }

    public void HidePanel()
    {
        if (!IsShown)
            return;
        IsShown = false;
        AppWindow.Hide();
        App.Instance.OnUiHidden();
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsShown)
            Update();
    }

    private void Update()
    {
        _updating = true;
        try
        {
            DeviceNameText.Text = Device.DeviceName;
            StatusView.Apply(StatusBar, RetryButton, Device);
            var connected = Device.IsConnected && Device.Mode is not null;
            BatteryText.Text = connected
                ? $"L {Percent(Device.LeftBattery)}  R {Percent(Device.RightBattery)}  ケース {Percent(Device.CaseBattery)}"
                : "";
            var codec = Assets.CodecIcon(connected ? Device.Codec?.Type : null);
            CodecIcon.Visibility = codec is null ? Visibility.Collapsed : Visibility.Visible;
            if (codec is not null && CodecIcon.UriSource != codec)
                CodecIcon.UriSource = codec;

            ModeSelector.IsEnabled = connected;
            // 切り替え中は、選んだモードの表示をそのまま残す（イヤホンの古い値で戻さない）
            if (!Device.IsApplying)
                ModeSelector.SelectedIndex = Device.Mode is { } mode ? (int)mode : -1;
            LevelCaption.Text = Device.Mode switch
            {
                UiMode.NoiseCanceling => "NC の強さ",
                UiMode.Transparent or UiMode.Attention => "外音の強さ",
                _ => "強さ",
            };
            LevelSlider.IsEnabled = connected && Device.Level is not null;
            if (Device.Level is { } level && !_commitTimer.IsEnabled)
                LevelSlider.Value = level;
            LevelText.Text = Device.Level?.ToString() ?? "—";
            StatusView.ApplySync(SyncIcon, SyncText, Device);
        }
        finally
        {
            _updating = false;
        }
    }

    private static string Percent(int? value) => value is { } v ? $"{v}%" : "—";

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

    private void OnLevelChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
            return;
        LevelText.Text = ((int)e.NewValue).ToString();
        Device.PreviewLevel((int)e.NewValue);
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => Device.Reconnect();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        HidePanel();
        App.Instance.ShowMainWindow();
    }
}

internal static class NativeCursor
{
    private struct Point
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    public static PointInt32 Position() => GetCursorPos(out var point) ? new PointInt32(point.X, point.Y) : default;
}
