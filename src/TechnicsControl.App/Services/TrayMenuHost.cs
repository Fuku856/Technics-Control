using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;

namespace TechnicsControl.App.Services;

/// <summary>
/// トレイの右クリックメニューを出すための、目に見えない小さなウィンドウ。
/// H.NotifyIcon の SecondWindow モードは開くたびにウィンドウを作り直してメニューを測るので遅い。
/// ここでは起動時に 1 回だけ作っておき、カーソル位置へ動かしてメニュー（WinUI の MenuFlyout）をその外側に出す。
/// </summary>
public sealed class TrayMenuHost
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int LwaAlpha = 0x2;

    private readonly Window _window;
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu;
    private readonly nint _hwnd;
    private readonly Stopwatch _openWatch = new();
    private bool _showPending;

    public TrayMenuHost(MenuFlyout menu, ThemeService theme)
    {
        _menu = menu;
        // 1×1 のウィンドウの外（画面上の好きな位置）にメニューを描かせる
        _menu.ShouldConstrainToRootBounds = false;
        // 右クリックから開くまでの時間（Debug ビルドの出力ウィンドウで確認する）
        _menu.Opened += (_, _) => Debug.WriteLine($"[TrayMenu] opened in {_openWatch.ElapsedMilliseconds} ms");
        _window = new Window { Content = _root, Title = "Technics Control menu" };
        // 終了でウィンドウを閉じたあとにメニューが閉じると、AppWindow はもう null になっている
        _menu.Closed += (_, _) => _window.AppWindow?.Hide();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _window.AppWindow.IsShownInSwitchers = false;
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }
        // ウィンドウ自体は透明にする（メニューは別のポップアップウィンドウなので影響しない）
        SetWindowLongPtr(_hwnd, GwlExStyle, GetWindowLongPtr(_hwnd, GwlExStyle) | WsExLayered | WsExToolWindow);
        SetLayeredWindowAttributes(_hwnd, 0, 0, LwaAlpha);
        // メニューの外をクリックして別のアプリに移ったら閉じる
        _window.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _menu.IsOpen)
                _menu.Hide();
        };
        theme.Register(_window);

        // XAML の中身は Activate するまで読み込まれない。最初の右クリックで待たないよう、画面外で一度だけ開いておく
        _window.AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 1, 1));
        _root.Loaded += (_, _) =>
        {
            if (_showPending)
            {
                _showPending = false;
                ShowMenu();
            }
            else
            {
                _window.AppWindow.Hide();
            }
        };
        _window.Activate();
    }

    public void Show(PointInt32 cursor)
    {
        _openWatch.Restart();
        _window.AppWindow.MoveAndResize(new RectInt32(cursor.X, cursor.Y, 1, 1));
        _window.AppWindow.Show();
        _window.Activate();
        // トレイから開いたメニューは、前面に出しておかないと外をクリックしても閉じない
        SetForegroundWindow(_hwnd);
        // 起動直後でまだ読み込み中なら、読み込みが終わってから出す
        if (_root.XamlRoot is null)
            _showPending = true;
        else
            ShowMenu();
    }

    private void ShowMenu()
    {
        _menu.ShowAt(_root, new FlyoutShowOptions
        {
            Position = new Point(0, 0),
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    public void Close() => _window.Close();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
