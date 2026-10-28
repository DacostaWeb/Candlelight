using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using LightBulb.Core;

namespace LightBulb.Views.Components;

public sealed class TransitionCurveControl : Control
{
    public static readonly StyledProperty<TransitionCurve> CurveProperty =
        AvaloniaProperty.Register<TransitionCurveControl, TransitionCurve>(
            nameof(Curve),
            TransitionCurve.Smooth,
            defaultBindingMode: BindingMode.TwoWay
        );
    public static readonly StyledProperty<double> FromTemperatureProperty =
        AvaloniaProperty.Register<TransitionCurveControl, double>(nameof(FromTemperature), 500);
    public static readonly StyledProperty<double> ToTemperatureProperty = AvaloniaProperty.Register<
        TransitionCurveControl,
        double
    >(nameof(ToTemperature), 2700);
    public static readonly StyledProperty<double> FromBrightnessProperty =
        AvaloniaProperty.Register<TransitionCurveControl, double>(nameof(FromBrightness), 100);
    public static readonly StyledProperty<double> ToBrightnessProperty = AvaloniaProperty.Register<
        TransitionCurveControl,
        double
    >(nameof(ToBrightness), 100);
    public static readonly StyledProperty<IBrush> TextBrushProperty = AvaloniaProperty.Register<
        TransitionCurveControl,
        IBrush
    >(nameof(TextBrush), Brushes.LightGray);
    public static readonly StyledProperty<IBrush> AccentBrushProperty = AvaloniaProperty.Register<
        TransitionCurveControl,
        IBrush
    >(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#D8B46E")));
    public static readonly StyledProperty<IBrush> GridBrushProperty = AvaloniaProperty.Register<
        TransitionCurveControl,
        IBrush
    >(nameof(GridBrush), new SolidColorBrush(Color.Parse("#44372C")));
    public TransitionCurve Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }
    public double FromTemperature
    {
        get => GetValue(FromTemperatureProperty);
        set => SetValue(FromTemperatureProperty, value);
    }
    public double ToTemperature
    {
        get => GetValue(ToTemperatureProperty);
        set => SetValue(ToTemperatureProperty, value);
    }
    public double FromBrightness
    {
        get => GetValue(FromBrightnessProperty);
        set => SetValue(FromBrightnessProperty, value);
    }
    public double ToBrightness
    {
        get => GetValue(ToBrightnessProperty);
        set => SetValue(ToBrightnessProperty, value);
    }
    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }
    public IBrush AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }
    public IBrush GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }
    private int _handle = 1;
    private bool _dragging;
    private Rect Plot =>
        new(46, 38, Math.Max(1, Bounds.Width - 82), Math.Max(1, Bounds.Height - 68));
    private bool Reverse =>
        ToTemperature < FromTemperature
        || (ToTemperature == FromTemperature && ToBrightness < FromBrightness);

    static TransitionCurveControl() =>
        AffectsRender<TransitionCurveControl>(
            CurveProperty,
            FromTemperatureProperty,
            ToTemperatureProperty,
            FromBrightnessProperty,
            ToBrightnessProperty,
            TextBrushProperty,
            AccentBrushProperty,
            GridBrushProperty
        );

    public TransitionCurveControl()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(
            this,
            "Arrasta as duas pegas. Com o teclado: espaço troca a pega; setas ajustam a curva."
        );
    }

    private Point PointAt(double x, double y) =>
        new(
            Plot.Left + x * Plot.Width,
            Reverse ? Plot.Top + y * Plot.Height : Plot.Bottom - y * Plot.Height
        );

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plot = Plot;
        if (Bounds.Width < 100 || Bounds.Height < 80)
            return;
        var curve = (Curve ?? TransitionCurve.Smooth).Normalize();
        void Text(string text, Point position, bool right = false, IBrush? brush = null)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                brush ?? TextBrush
            );
            context.DrawText(
                formatted,
                right ? position.WithX(position.X - formatted.Width) : position
            );
        }
        Text($"{FromTemperature:0} K / {FromBrightness:0}%", new(0, 4));
        Text($"{ToTemperature:0} K / {ToBrightness:0}%", new(Bounds.Width, 4), true);
        var low = Math.Min(FromTemperature, ToTemperature);
        var high = Math.Max(FromTemperature, ToTemperature);
        for (var step = 0; step <= 2; step++)
        {
            var p = step / 2d;
            var y = plot.Bottom - plot.Height * p;
            context.DrawLine(new Pen(GridBrush, 1), new(plot.Left, y), new(plot.Right, y));
            Text($"{low + (high - low) * p:0}", new(plot.Left - 6, y - 7), true);
            Text($"{p * 100:0}%", new(plot.Right + 4, y - 7));
        }
        var temperaturePath = new StreamGeometry();
        var brightnessPath = new StreamGeometry();
        using (var temperature = temperaturePath.Open())
        using (var brightness = brightnessPath.Open())
        {
            for (var i = 0; i <= 100; i++)
            {
                var x = i / 100d;
                var progress = curve.Evaluate(x);
                var t = FromTemperature + (ToTemperature - FromTemperature) * progress;
                var b = FromBrightness + (ToBrightness - FromBrightness) * progress;
                var tp = new Point(
                    plot.Left + x * plot.Width,
                    high > low
                        ? plot.Bottom - (t - low) / (high - low) * plot.Height
                        : plot.Center.Y
                );
                var bp = new Point(
                    plot.Left + x * plot.Width,
                    plot.Bottom - Math.Clamp(b / 100, 0, 1) * plot.Height
                );
                if (i == 0)
                {
                    temperature.BeginFigure(tp, false);
                    brightness.BeginFigure(bp, false);
                }
                else
                {
                    temperature.LineTo(tp);
                    brightness.LineTo(bp);
                }
            }
            temperature.EndFigure(false);
            brightness.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(TextBrush, 1.5, DashStyle.Dash), brightnessPath);
        context.DrawGeometry(null, new Pen(AccentBrush, 2.5), temperaturePath);
        var h1 = PointAt(curve.X1, curve.Y1);
        var h2 = PointAt(curve.X2, curve.Y2);
        context.DrawLine(new Pen(GridBrush, 1.5), PointAt(0, 0), h1);
        context.DrawLine(new Pen(GridBrush, 1.5), PointAt(1, 1), h2);
        context.DrawEllipse(
            AccentBrush,
            new Pen(TextBrush, IsFocused && _handle == 1 ? 3 : 1),
            h1,
            6,
            6
        );
        context.DrawEllipse(
            AccentBrush,
            new Pen(TextBrush, IsFocused && _handle == 2 ? 3 : 1),
            h2,
            6,
            6
        );
        Text("Cor", new(plot.Left, plot.Bottom + 10), brush: AccentBrush);
        Text("Brilho · tracejado", new(plot.Right, plot.Bottom + 10), true);
    }

    internal void MoveHandle(int handle, double x, double y)
    {
        var c = (Curve ?? TransitionCurve.Smooth).Normalize();
        var changed =
            handle == 1
                ? c with
                {
                    X1 = Math.Clamp(x, 0, c.X2),
                    Y1 = Math.Clamp(y, 0, c.Y2),
                }
                : c with
                {
                    X2 = Math.Clamp(x, c.X1, 1),
                    Y2 = Math.Clamp(y, c.Y1, 1),
                };
        SetCurrentValue(CurveProperty, changed);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var p = e.GetPosition(this);
        var c = (Curve ?? TransitionCurve.Smooth).Normalize();
        static double Distance(Point a, Point b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var d1 = Distance(p, PointAt(c.X1, c.Y1));
        var d2 = Distance(p, PointAt(c.X2, c.Y2));
        if (Math.Min(d1, d2) > 24)
            return;
        _handle = d1 <= d2 ? 1 : 2;
        _dragging = true;
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        var p = e.GetPosition(this);
        MoveHandle(
            _handle,
            (p.X - Plot.Left) / Plot.Width,
            Reverse ? (p.Y - Plot.Top) / Plot.Height : (Plot.Bottom - p.Y) / Plot.Height
        );
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _dragging = false;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var c = (Curve ?? TransitionCurve.Smooth).Normalize();
        var x = _handle == 1 ? c.X1 : c.X2;
        var y = _handle == 1 ? c.Y1 : c.Y2;
        switch (e.Key)
        {
            case Key.Space:
                _handle = _handle == 1 ? 2 : 1;
                break;
            case Key.Left:
                MoveHandle(_handle, x - 0.02, y);
                break;
            case Key.Right:
                MoveHandle(_handle, x + 0.02, y);
                break;
            case Key.Up:
                MoveHandle(_handle, x, y + (Reverse ? -0.02 : 0.02));
                break;
            case Key.Down:
                MoveHandle(_handle, x, y + (Reverse ? 0.02 : -0.02));
                break;
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }
}
