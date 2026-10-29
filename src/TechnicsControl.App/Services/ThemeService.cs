using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TechnicsControl.App.Services;

/// <summary>
/// アプリのテーマ（Windows に合わせる／ライト／ダーク）を各ウィンドウに反映し、
/// Windows 側のテーマ変更（アプリ用とタスクバー用）を監視する。
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly UISettings _uiSettings = new();
    private readonly List<Window> _windows = [];

    public ThemeService(SettingsStore settings, DispatcherQueue dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _uiSettings.ColorValuesChanged += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            ApplyAll();
            TaskbarThemeChanged?.Invoke();
        });
    }

    /// <summary>タスクバーのテーマが変わったかもしれないとき（トレイアイコンの色を合わせ直す）。</summary>
    public event Action? TaskbarThemeChanged;

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            _settings.Theme = value;
            _settings.Save();
            ApplyAll();
        }
    }

    /// <summary>実際に使っているテーマ（「Windows に合わせる」のときは Windows のアプリ用の設定）。</summary>
    public ElementTheme EffectiveTheme => Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ReadPersonalize("AppsUseLightTheme") ? ElementTheme.Light : ElementTheme.Dark,
    };

    /// <summary>タスクバーが明るいテーマか（アプリのテーマとは別に設定できる）。</summary>
    public static bool IsTaskbarLight => ReadPersonalize("SystemUsesLightTheme");

    public void Register(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    private void ApplyAll()
    {
        foreach (var window in _windows.ToList())
            Apply(window);
    }

    private void Apply(Window window)
    {
        var theme = EffectiveTheme;
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme;

        // タイトルバーのボタン（最小化・閉じるなど）の色も合わせる
        var titleBar = window.AppWindow.TitleBar;
        var dark = theme == ElementTheme.Dark;
        titleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A) : Color.FromArgb(0xFF, 0x76, 0x76, 0x76);
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
    }

    private static bool ReadPersonalize(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is not int value || value != 0;
    }
}
