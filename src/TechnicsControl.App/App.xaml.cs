using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TechnicsControl.App.Services;
using TechnicsControl.App.Views;

namespace TechnicsControl.App;

public partial class App : Application
{
    // 「ウィンドウを閉じているあいだは接続を切る」設定のとき、隠してから切るまでの時間
    private static readonly TimeSpan IdleDisconnectDelay = TimeSpan.FromSeconds(60);

    private QuickPanelWindow? _quickPanel;
    private TrayService? _tray;
    private DispatcherQueueTimer? _idleTimer;

    public App()
    {
        Instance = this;
        InitializeComponent();
        // ウィンドウをすべて閉じてもトレイに常駐し続ける
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    public static App Instance { get; private set; } = null!;

    public static SettingsStore Settings { get; private set; } = null!;

    public static DeviceService Device { get; private set; } = null!;

    public static ThemeService Theme { get; private set; } = null!;

    public static bool IsExiting { get; private set; }

    public MainWindow? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Settings = SettingsStore.Load();
        Theme = new ThemeService(Settings, dispatcher);
        Device = new DeviceService(dispatcher);
        Device.Start();
        _tray = new TrayService(Device, Theme);
        _idleTimer = dispatcher.CreateTimer();
        _idleTimer.Interval = IdleDisconnectDelay;
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += async (_, _) =>
        {
            if (Settings.DisconnectWhenHidden && !IsUiVisible)
                await Device.PauseAsync();
        };

        // 2 つ目の起動（スタートメニューから開き直したときなど）は、ウィンドウを開く要求として受け取る
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(ShowMainWindow);

        // ホームの本体画像は、ウィンドウを出す前に読み込んでおく（出てから遅れて表示されないように）
        await Assets.PreloadDeviceBitmapsAsync(Settings.BodyColor);
        if (!Environment.GetCommandLineArgs().Contains(StartupService.BackgroundArgument))
            ShowMainWindow();
    }

    private bool IsUiVisible => MainWindow?.AppWindow.IsVisible == true || _quickPanel?.IsShown == true;

    public void ShowMainWindow() => ShowMainWindow(hideQuickPanel: false);

    public void ShowMainWindow(bool hideQuickPanel)
    {
        if (hideQuickPanel)
            _quickPanel?.HidePanel();
        if (MainWindow is null)
        {
            MainWindow = new MainWindow();
            Theme.Register(MainWindow);
        }
        MainWindow.BringToFront();
        OnUiShown();
    }

    public void ToggleQuickPanel()
    {
        if (_quickPanel is null)
        {
            _quickPanel = new QuickPanelWindow();
            Theme.Register(_quickPanel);
        }
        if (_quickPanel.IsShown)
        {
            _quickPanel.HidePanel();
            return;
        }
        _quickPanel.ShowPanel();
        OnUiShown();
    }

    public void OnMainWindowHidden() => OnUiHidden();

    public void OnUiHidden()
    {
        Device.IsUiVisible = IsUiVisible;
        if (Settings.DisconnectWhenHidden && !IsUiVisible)
            _idleTimer?.Start();
    }

    private void OnUiShown()
    {
        Device.IsUiVisible = true;
        _idleTimer?.Stop();
        if (Device.Paused)
            Device.Reconnect();
    }

    public void ExitApp()
    {
        IsExiting = true;
        Device.Stop();
        _tray?.Dispose();
        _quickPanel?.Close();
        MainWindow?.Close();
        Exit();
    }
}
