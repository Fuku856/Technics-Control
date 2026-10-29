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
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(560 * scale);
            presenter.PreferredMinimumHeight = (int)(520 * scale);
        }

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

        Nav.SelectedItem = Nav.MenuItems[0];
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
}
