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
/// </summary>
public sealed partial class CircularDial : UserControl
{
    public const int Minimum = 1;
    public const int Maximum = 100;
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    private readonly DispatcherTimerHelper _commitTimer;
    private bool _dragging;

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

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;
        Focus(FocusState.Pointer);
        _dragging = Root.CapturePointer(e.Pointer);
        SetFromPoint(e.GetCurrentPoint(Root).Position);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
            SetFromPoint(e.GetCurrentPoint(Root).Position);
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
        _commitTimer.Stop();
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

    private void SetFromPoint(Point point)
    {
        var (cx, cy, _) = Geometry();
        var angle = Math.Atan2(point.Y - cy, point.X - cx) * 180 / Math.PI;
        var offset = ((angle - StartAngle) % 360 + 360) % 360;
        if (offset > SweepAngle)
            offset = offset < SweepAngle + (360 - SweepAngle) / 2 ? SweepAngle : 0; // 下の切れ目は近い端に寄せる
        var value = Minimum + (int)Math.Round(offset / SweepAngle * (Maximum - Minimum));
        if (value == Value)
            return;
        Value = value;
        ValueChanging?.Invoke(this, value);
    }

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
    }
}
