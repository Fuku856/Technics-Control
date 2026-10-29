using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace TechnicsControl.App.Controls;

/// <summary>
/// 強さの円形ダイヤル。公式アプリの CustomCircularRange と同じく、270 度の円弧で 1〜100 を選ぶ。
/// ドラッグ中は <see cref="ValueChanging"/>、確定（離す・キー操作のあと少し待つ）で <see cref="ValueCommitted"/>。
/// 操作を始められるのは円弧の上だけ。真ん中の数字などに触れただけでは値は変わらない。
/// </summary>
public sealed partial class CircularDial : UserControl
{
    public const int Minimum = 1;
    public const int Maximum = 100;
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    /// <summary>円弧からこの距離（px）までの押下を、ダイヤルの操作として受け付ける。</summary>
    private const double HitBand = 26;

    private readonly DispatcherTimerHelper _commitTimer;
    private bool _dragging;
    private bool _changedWhileDragging;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(CircularDial), new PropertyMetadata(Maximum, (d, _) => ((CircularDial)d).UpdateVisuals()));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(CircularDial), new PropertyMetadata("", (d, e) => ((CircularDial)d).CaptionText.Text = (string)e.NewValue));

    public CircularDial()
    {
        InitializeComponent();
        _commitTimer = new DispatcherTimerHelper(TimeSpan.FromMilliseconds(400), () => ValueCommitted?.Invoke(this, Value));
        Root.SizeChanged += (_, _) => UpdateVisuals();
        Root.PointerPressed += OnPointerPressed;
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        Root.PointerCaptureLost += (_, _) => EndDrag();
        Root.PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
        IsEnabledChanged += (_, _) => UpdateVisuals();
        UpdateVisuals();
    }

    /// <summary>ドラッグ中など、値が変わっている途中。</summary>
    public event EventHandler<int>? ValueChanging;

    /// <summary>値が確定した。</summary>
    public event EventHandler<int>? ValueCommitted;

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public bool IsDragging => _dragging;

    /// <summary>ドラッグ中か、ホイールやキーで動かした値をまだ確定していない。外から値を上書きしないこと。</summary>
    public bool IsInteracting => _dragging || _commitTimer.IsRunning;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;
        var point = e.GetCurrentPoint(Root).Position;
        var (cx, cy, r) = Geometry();
        var onKnob = Distance(point, KnobCenter()) <= Knob.Width / 2 + 6;
        // 円弧から離れた場所（真ん中の数字や下の切れ目）では操作を始めない
        if (!onKnob && (Math.Abs(Distance(point, new Point(cx, cy)) - r) > HitBand || OffsetAt(point) > SweepAngle))
            return;
        Focus(FocusState.Pointer);
        _changedWhileDragging = false;
        _dragging = Root.CapturePointer(e.Pointer);
        // つまみをつかんだときは今の値から動かす。円弧の別の場所なら、押した位置の値にする
        if (!onKnob)
            SetFromPoint(point, allowJump: true);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
            SetFromPoint(e.GetCurrentPoint(Root).Position, allowJump: false);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Root.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;
        _dragging = false;
        // ホイールやキーで動かした値の確定待ちがあれば、ここでまとめて確定する
        var pending = _commitTimer.IsRunning;
        _commitTimer.Stop();
        // つまみに触れただけ（一度も値が変わっていない）なら、イヤホンには送らない
        if (_changedWhileDragging || pending)
            ValueCommitted?.Invoke(this, Value);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        Step(delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = e.Key switch
        {
            VirtualKey.Right or VirtualKey.Up => 1,
            VirtualKey.Left or VirtualKey.Down => -1,
            VirtualKey.PageUp => 10,
            VirtualKey.PageDown => -10,
            VirtualKey.Home => Minimum - Value,
            VirtualKey.End => Maximum - Value,
            _ => 0,
        };
        if (step == 0 || !IsEnabled)
            return;
        Step(step);
        e.Handled = true;
    }

    private void Step(int delta)
    {
        var next = Math.Clamp(Value + delta, Minimum, Maximum);
        if (next == Value)
            return;
        Value = next;
        ValueChanging?.Invoke(this, Value);
        _commitTimer.Restart();
    }

    private void SetFromPoint(Point point, bool allowJump)
    {
        var offset = OffsetAt(point);
        var nearEnd = Value > (Minimum + Maximum) / 2 ? Maximum : Minimum;
        int value;
        if (offset > SweepAngle)
        {
            // 下の切れ目では、今の値に近いほうの端で止める（Max と Min のあいだを行き来しない）
            value = nearEnd;
        }
        else
        {
            value = Minimum + (int)Math.Round(offset / SweepAngle * (Maximum - Minimum));
            // ドラッグ中に切れ目を越えて反対の端へ飛んだときも、近いほうの端で止める
            if (!allowJump && Math.Abs(value - Value) > (Maximum - Minimum) / 2)
                value = nearEnd;
        }
        if (value == Value)
            return;
        Value = value;
        _changedWhileDragging = true;
        ValueChanging?.Invoke(this, value);
    }

    /// <summary>円弧の始点（Min）から測った、点の角度（0〜360 度。SweepAngle より大きければ下の切れ目）。</summary>
    private double OffsetAt(Point point)
    {
        var (cx, cy, _) = Geometry();
        var angle = Math.Atan2(point.Y - cy, point.X - cx) * 180 / Math.PI;
        return ((angle - StartAngle) % 360 + 360) % 360;
    }

    private Point KnobCenter()
    {
        var (cx, cy, r) = Geometry();
        return PointAt(cx, cy, r, StartAngle + SweepAngle * (Value - Minimum) / (Maximum - Minimum));
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private (double Cx, double Cy, double Radius) Geometry()
    {
        var width = Root.ActualWidth > 0 ? Root.ActualWidth : Root.Width;
        var height = Root.ActualHeight > 0 ? Root.ActualHeight : Root.Height;
        var radius = Math.Min(width, height + 30) / 2 - 20;
        return (width / 2, radius + 16, radius);
    }

    private void UpdateVisuals()
    {
        if (TrackPath is null)
            return;
        var (cx, cy, r) = Geometry();
        var fraction = (double)(Value - Minimum) / (Maximum - Minimum);
        TrackPath.Data = Arc(cx, cy, r, StartAngle, SweepAngle);
        ValuePath.Data = Arc(cx, cy, r, StartAngle, Math.Max(0.5, SweepAngle * fraction));
        var knob = PointAt(cx, cy, r, StartAngle + SweepAngle * fraction);
        Canvas.SetLeft(Knob, knob.X - Knob.Width / 2);
        Canvas.SetTop(Knob, knob.Y - Knob.Height / 2);
        ValueText.Text = IsEnabled ? Value.ToString() : "—";
        ValuePath.Opacity = Knob.Opacity = IsEnabled ? 1 : 0;
        AutomationProperties.SetHelpText(this, $"{Value}");
    }

    private static Point PointAt(double cx, double cy, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static PathGeometry Arc(double cx, double cy, double r, double start, double sweep)
    {
        var figure = new PathFigure { StartPoint = PointAt(cx, cy, r, start), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(cx, cy, r, start + sweep),
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweep > 180,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>最後の操作から一定時間たったら 1 回だけ実行するタイマー。</summary>
    private sealed class DispatcherTimerHelper
    {
        private readonly DispatcherTimer _timer = new();

        public DispatcherTimerHelper(TimeSpan interval, Action action)
        {
            _timer.Interval = interval;
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                action();
            };
        }

        public void Restart()
        {
            _timer.Stop();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        public bool IsRunning => _timer.IsEnabled;
    }
}
