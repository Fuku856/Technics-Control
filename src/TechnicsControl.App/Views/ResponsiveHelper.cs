using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TechnicsControl.App.Views;

/// <summary>
/// ページの幅に合わせたレイアウトの補助（各ページで共通）。
/// </summary>
internal static class ResponsiveHelper
{
    /// <summary>これより狭いページでは余白を詰める。</summary>
    public const double CompactPageWidth = 560;

    private static readonly Thickness NormalPadding = new(36, 8, 36, 36);
    private static readonly Thickness CompactPadding = new(16, 8, 16, 24);

    /// <summary>
    /// ScrollViewer の中の、MaxWidth を付けたページ本体を幅に追従させる。
    /// ScrollViewer は中身の「希望の幅」が表示幅より狭いと、その幅で中央に寄せて描く。MaxWidth 付きの StackPanel は
    /// 実際にはもっと広く描かれるので右にずれて端が切れる。幅を「表示幅（上限は MaxWidth）」に固定してそれを防ぐ。
    /// <paramref name="onWidthChanged"/> には、余白を除いた中身の幅が渡る（ページごとの狭いとき用の切り替えに使う）。
    /// </summary>
    public static void Attach(ScrollViewer scroller, StackPanel root, Action<double>? onWidthChanged = null)
    {
        var maxWidth = root.MaxWidth;
        root.HorizontalAlignment = HorizontalAlignment.Center;
        scroller.SizeChanged += (_, e) =>
        {
            var width = Math.Min(e.NewSize.Width, maxWidth);
            root.Width = width;
            root.Padding = e.NewSize.Width < CompactPageWidth ? CompactPadding : NormalPadding;
            onWidthChanged?.Invoke(width - root.Padding.Left - root.Padding.Right);
        };
    }

    /// <summary>
    /// 狭いときの Segmented。4 つが同じ幅に並ぶので、「トランスペアレント」のような長い名前が切れる。
    /// 狭いときはアイコンを隠して文字を小さくし、名前を全部見せる。
    /// （Icon を外して付け直すと、項目のテンプレートがアイコンを描き直さないので、Visibility で隠す）
    /// </summary>
    public static void SetCompact(Segmented segmented, bool compact)
    {
        foreach (var item in segmented.Items.OfType<SegmentedItem>())
        {
            if (item.Icon is { } icon)
                icon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (compact)
            {
                item.FontSize = 12;
                item.Padding = new Thickness(6, 4, 6, 4);
            }
            else
            {
                item.ClearValue(Control.FontSizeProperty);
                item.ClearValue(Control.PaddingProperty);
            }
        }
    }

    /// <summary>Segmented の項目を、アイコン付きで全部の名前が収まるのに必要な幅（DIP。1 項目 約 170）。</summary>
    public const double SegmentedNormalMinWidth = 680;
}
