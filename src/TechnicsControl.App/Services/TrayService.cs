using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using TechnicsControl.App.Views;

namespace TechnicsControl.App.Services;

/// <summary>
/// タスクトレイのアイコン。アイコンは今のモードを表し、タスクバーのテーマ（明るい／暗い）に合わせて色を変える。
/// 左クリックでクイックパネル、ダブルクリックでメインウィンドウ、右クリックでメニュー。
/// トレイからの操作は DispatcherQueue 経由で呼ばれ、例外がそのままだとプロセスごと落ちるので、すべて ErrorLog.Guard で包む。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly DeviceService _device;
    private readonly TaskbarIcon _icon;
    private readonly TrayMenuHost _menuHost;
    private readonly List<RadioMenuFlyoutItem> _modeItems = [];
    private string? _iconPath;
    private long _lastDoubleClick;

    public TrayService(DeviceService device, ThemeService theme)
    {
        _device = device;
        _menuHost = new TrayMenuHost(BuildMenu(), theme);
        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => ErrorLog.Guard(OnLeftClick)),
            DoubleClickCommand = new RelayCommand(() => ErrorLog.Guard(OnDoubleClick)),
            RightClickCommand = new RelayCommand(() => ErrorLog.Guard(ShowMenu)),
        };
        Update();
        _icon.ForceCreate(enablesEfficiencyMode: false);

        device.PropertyChanged += (_, _) => Update();
        theme.TaskbarThemeChanged += Update;
    }

    /// <summary>右クリックメニューをカーソル位置に出す。</summary>
    public void ShowMenu() => _menuHost.Show(NativeCursor.Position());

    private void OnLeftClick()
    {
        // NoLeftClickDelay なので、ダブルクリックの 2 回目の押し上げも単クリックとして届く。それは無視する
        if (Environment.TickCount64 - _lastDoubleClick < GetDoubleClickTime())
            return;
        App.Instance.ToggleQuickPanel();
    }

    private void OnDoubleClick()
    {
        _lastDoubleClick = Environment.TickCount64;
        // 1 回目のクリックでクイックパネルが開くので、閉じてからメインウィンドウを手前に出す
        App.Instance.ShowMainWindow(hideQuickPanel: true);
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Technics Control を開く", Command = new RelayCommand(() => ErrorLog.Guard(App.Instance.ShowMainWindow)) };
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var mode in Enum.GetValues<UiMode>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = DeviceService.ModeName(mode),
                GroupName = "Mode",
                Command = new AsyncRelayCommand(() => ErrorLog.GuardAsync(() => _device.SetModeAsync(mode))),
            };
            _modeItems.Add(item);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "再接続", Command = new RelayCommand(() => ErrorLog.Guard(_device.Reconnect)) });
        menu.Items.Add(new MenuFlyoutItem { Text = "終了", Command = new RelayCommand(() => ErrorLog.Guard(App.Instance.ExitApp)) });
        return menu;
    }

    private void Update()
    {
        var mode = _device.IsConnected ? _device.Mode : null;
        var path = Assets.TrayIconPath(mode, ThemeService.IsTaskbarLight);
        if (path != _iconPath && File.Exists(path))
        {
            var previous = _icon.Icon;
            _icon.Icon = new System.Drawing.Icon(path);
            previous?.Dispose();
            _iconPath = path;
        }

        _icon.ToolTipText = _device.IsConnected && mode is { } m
            ? $"{_device.DeviceName}\n{DeviceService.ModeName(m)}{(_device.Level is { } level ? $" {level}" : "")}\nL {Percent(_device.LeftBattery)} / R {Percent(_device.RightBattery)}"
            : $"{_device.DeviceName}\n{_device.StatusMessage}";

        for (var i = 0; i < _modeItems.Count; i++)
        {
            _modeItems[i].IsChecked = mode == (UiMode)i;
            _modeItems[i].IsEnabled = _device.IsConnected;
        }
    }

    private static string Percent(int? value) => value is { } v ? $"{v}%" : "—";

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    public void Dispose()
    {
        _icon.Dispose();
        _menuHost.Close();
    }
}
