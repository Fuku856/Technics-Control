using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.App.Views.Pages;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace TechnicsControl.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Home"] = typeof(HomePage),
        ["Outside"] = typeof(OutsideControlPage),
        ["Sound"] = typeof(SoundModePage),
        ["Settings"] = typeof(SettingsPage),
    };

    // 設定から開く下の階層のページ。履歴で戻ってきたときも、左のメニューは設定を選んでおく
    private static readonly Dictionary<Type, string> SubPageParents = new()
    {
        [typeof(KeyCustomizePage)] = "Settings",
        [typeof(ProductInfoPage)] = "Settings",
    };

    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Assets.AppIconPath);
        TitleBarIcon.Source = new BitmapImage(new Uri(Assets.AppIconPath));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        var scale = Content.XamlRoot?.RasterizationScale ?? GetDpiScale();
        AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(760 * scale)));
        ApplyScale(scale);
        // 拡大率の違うモニターへ移したときは、最小サイズとタイトルバーの余白を付け直す
        RootGrid.Loaded += (_, _) => RootGrid.XamlRoot.Changed += (_, _) => ApplyScale(RootGrid.XamlRoot.RasterizationScale);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange)
                UpdateTitleBarInsets();
        };

        // 閉じるボタンではトレイに隠すだけにする（終了はトレイのメニューから）
        AppWindow.Closing += (_, args) =>
        {
            if (App.IsExiting)
                return;
            args.Cancel = true;
            AppWindow.Hide();
            App.Instance.OnMainWindowHidden();
        };

        // マウスの戻る／進むボタン、Alt+←／→、キーボードの戻る／進むキーでもページの履歴をたどる。
        // スライダーやダイヤルが矢印キーを使う前に受け取れるよう、PreviewKeyDown で見る
        RootGrid.PreviewKeyDown += OnPreviewKeyDown;
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);

        // 狭いとき（Minimal）はメニューのボタンがページの上に重なるので、その分ページを下げる
        Nav.DisplayModeChanged += (_, args) => UpdateContentMargin(args.DisplayMode);
        Nav.Loaded += (_, _) => UpdateContentMargin(Nav.DisplayMode);

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>隠していても最小化していても、手前に出してフォーカスを移す。</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        // Activate だけでは、ほかのアプリのウィンドウの後ろに残ることがある
        NativeMethods.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void UpdateContentMargin(NavigationViewDisplayMode mode) =>
        ContentFrame.Margin = mode == NavigationViewDisplayMode.Minimal ? new Thickness(0, 44, 0, 0) : new Thickness(0);

    private double _scale = 1;

    private void ApplyScale(double scale)
    {
        _scale = scale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(560 * scale);
            presenter.PreferredMinimumHeight = (int)(520 * scale);
        }
        UpdateTitleBarInsets();
    }

    /// <summary>タイトルの文字が最小化・閉じるボタンの下に潜らないよう、ボタンの実際の幅だけ右を空ける。</summary>
    private void UpdateTitleBarInsets()
    {
        var titleBar = AppWindow.TitleBar;
        var left = titleBar.LeftInset / _scale;
        var right = titleBar.RightInset / _scale;
        if (right > 0)
            AppTitleBar.Padding = new Thickness(16 + left, 0, right, 0);
    }

    public void Navigate(string tag)
    {
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
    }

    public void NavigateSub(Type page, object? parameter = null) =>
        ContentFrame.Navigate(page, parameter, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_syncingSelection && args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page)
            && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => TryGoBack();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
        var handled = e.Key switch
        {
            VirtualKey.GoBack => TryGoBack(),
            VirtualKey.GoForward => TryGoForward(),
            VirtualKey.Left when alt => TryGoBack(),
            VirtualKey.Right when alt => TryGoForward(),
            _ => false,
        };
        if (handled)
            e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(RootGrid).Properties;
        if ((properties.IsXButton1Pressed && TryGoBack()) || (properties.IsXButton2Pressed && TryGoForward()))
            e.Handled = true;
    }

    private bool TryGoBack()
    {
        if (!ContentFrame.CanGoBack)
            return false;
        ContentFrame.GoBack();
        return true;
    }

    private bool TryGoForward()
    {
        if (!ContentFrame.CanGoForward)
            return false;
        ContentFrame.GoForward();
        return true;
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        Nav.IsBackEnabled = ContentFrame.CanGoBack;
        var tag = Pages.FirstOrDefault(p => p.Value == e.SourcePageType).Key ?? SubPageParents.GetValueOrDefault(e.SourcePageType);
        if (tag is null)
            return;
        // 表示中のページに合わせて選び直すだけなので、選択の変更でもう一度ページを開かない
        _syncingSelection = true;
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
        _syncingSelection = false;
    }

    private double GetDpiScale() => NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hwnd);

    /// <summary>その点があるモニターの DPI。</summary>
    internal static uint GetDpiForPoint(PointInt32 point)
    {
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96;
    }

    private const uint MonitorDefaultToNearest = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(PointInt32 point, uint flags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
