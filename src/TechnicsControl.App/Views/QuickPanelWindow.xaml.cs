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
    // モードの 4 つのボタンに「トランスペアレント」まで全部収まる幅（狭い表示: 1 つ約 120）
    private const int PanelWidth = 520;
    private const int MinPanelHeight = 200;
    private const int FirstShowHeight = 300;
    private const int ScreenMargin = 12;
    // フォーカスが外れて閉じた直後のトレイのクリックは「閉じる」とみなす時間
    private const int AutoHideGraceMs = 300;

    private readonly DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _updating;
    private long? _autoHiddenAt;
    private RectInt32 _workArea;
    private double _scale = 1;

    private DeviceService Device => App.Device;

    public QuickPanelWindow()
    {
        InitializeComponent();
        LevelSlider.ValueChanged += OnLevelChanged;
        ResponsiveHelper.SetCompact(ModeSelector, true);
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
            if (args.WindowActivationState == WindowActivationState.Deactivated && IsShown)
            {
                _autoHiddenAt = Environment.TickCount64;
                HidePanel();
            }
        };
        // InfoBar の開閉などで中身の高さが変わったら、ウィンドウの高さを合わせ直す
        RootGrid.SizeChanged += (_, _) => FitHeight();
        _commitTimer.Tick += async (_, _) =>
        {
            _commitTimer.Stop();
            await Device.CommitLevelAsync((int)LevelSlider.Value);
        };
        Device.PropertyChanged += OnDeviceChanged;
        Closed += (_, _) => Device.PropertyChanged -= OnDeviceChanged;
    }

    public bool IsShown { get; private set; }

    /// <summary>フォーカスが外れて自動で閉じたばかりか。</summary>
    public bool WasJustAutoHidden => Environment.TickCount64 - _autoHiddenAt < AutoHideGraceMs;

    public void ShowPanel()
    {
        // カーソルのあるディスプレイの作業領域の右下（タスクバーの上）に出す。
        // 拡大率は今いるモニターではなく、出す先のモニターのものを使う
        var cursor = NativeCursor.Position();
        _workArea = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Primary).WorkArea;
        _scale = NativeMethods.GetDpiForPoint(cursor) / 96.0;
        Update();
        Place(MeasureHeight());
        AppWindow.Show();
        Activate();
        IsShown = true;
        _ = Device.RefreshAsync();
    }

    /// <summary>中身に必要な高さ（DIP）。初めて開くときはまだ測れないので仮の高さにし、読み込み後の SizeChanged で合わせ直す。</summary>
    private double MeasureHeight()
    {
        if (RootGrid.XamlRoot is null)
            return FirstShowHeight;
        RootGrid.Measure(new Windows.Foundation.Size(PanelWidth, double.PositiveInfinity));
        return Math.Max(MinPanelHeight, RootGrid.DesiredSize.Height);
    }

    private void FitHeight()
    {
        if (!IsShown)
            return;
        var height = MeasureHeight();
        if (Math.Abs(height * _scale - AppWindow.ClientSize.Height) > 1)
            Place(height);
    }

    private void Place(double height)
    {
        // MoveAndResize は枠を含めた大きさなので、枠の分を足す
        var borderWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var borderHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        var width = (int)Math.Ceiling(PanelWidth * _scale) + borderWidth;
        var margin = (int)(ScreenMargin * _scale);
        var pixels = Math.Min((int)Math.Ceiling(height * _scale) + borderHeight, _workArea.Height - margin * 2);
        AppWindow.MoveAndResize(new RectInt32(
            _workArea.X + _workArea.Width - width - margin, _workArea.Y + _workArea.Height - pixels - margin, width, pixels));
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
                UiMode.Transparent => "外音の強さ",
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
