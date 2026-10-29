using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.App.Views.Pages;
using Windows.Graphics;

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
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page)
            && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        Nav.IsBackEnabled = ContentFrame.CanGoBack;
        var tag = Pages.FirstOrDefault(p => p.Value == e.SourcePageType).Key;
        if (tag is not null)
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
    }

    private double GetDpiScale() => NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);
}
