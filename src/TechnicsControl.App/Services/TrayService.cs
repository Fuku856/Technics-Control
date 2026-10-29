using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace TechnicsControl.App.Services;

/// <summary>
/// タスクトレイのアイコン。アイコンは今のモードを表し、タスクバーのテーマ（明るい／暗い）に合わせて色を変える。
/// 左クリックでクイックパネル、右クリックでメニュー。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly DeviceService _device;
    private readonly TaskbarIcon _icon;
    private readonly List<RadioMenuFlyoutItem> _modeItems = [];
    private string? _iconPath;

    public TrayService(DeviceService device, ThemeService theme)
    {
        _device = device;
        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            ContextMenuMode = ContextMenuMode.SecondWindow,
            LeftClickCommand = new RelayCommand(() => App.Instance.ToggleQuickPanel()),
            ContextFlyout = BuildMenu(),
        };
        Update();
        _icon.ForceCreate(enablesEfficiencyMode: false);

        device.PropertyChanged += (_, _) => Update();
        theme.TaskbarThemeChanged += Update;
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Technics Control を開く", Command = new RelayCommand(() => App.Instance.ShowMainWindow()) };
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var mode in Enum.GetValues<UiMode>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = DeviceService.ModeName(mode),
                GroupName = "Mode",
                Command = new AsyncRelayCommand(() => _device.SetModeAsync(mode)),
            };
            _modeItems.Add(item);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "再接続", Command = new RelayCommand(_device.Reconnect) });
        menu.Items.Add(new MenuFlyoutItem { Text = "終了", Command = new RelayCommand(() => App.Instance.ExitApp()) });
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

    public void Dispose() => _icon.Dispose();
}
