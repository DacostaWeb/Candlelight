using System.Drawing.Drawing2D;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class ScheduleGraph : Control
{
    private SchedulePlan _plan = new();
    private MonitorProfile? _monitor;
    private MonitorProfile[] _others = [];
    private DateOnly _date;
    private ResolvedTiming[] _nodes = [];
    private ScheduledColorEvent[] _events = [];
    private string? _selected;
    private bool _brightness;
    private int _hoverX = -1;
    private TimingPoint? _dragPoint;
    private ColorProfile? _dragColor;
    private Point _dragStart;
    private bool _moved;
    private bool _compare;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _staticPreview;

    internal event Action<string>? PointSelected;
    internal event Action<TimingPoint, ColorProfile>? PointEdited;
    internal event Action<TimeOnly, double>? PointAdded;
    internal event Action<string>? PointRemoved;
    internal event Action? EditRequested;
    internal event Action? CurveRequested;

    public ScheduleGraph()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable,
            true
        );
        BackColor = LightTheme.Background;
        ForeColor = LightTheme.Text;
        TabStop = true;
        AccessibleRole = AccessibleRole.Diagram;
        AccessibleName =
            "Horário diário. Arrasta os pontos; setas alteram hora e valor; Delete remove; Escape cancela.";
        ResizeRedraw = true;
    }

    internal void SetData(
        SchedulePlan plan,
        MonitorProfile monitor,
        IEnumerable<MonitorProfile> others,
        DateOnly date,
        string? selected,
        bool brightness,
        bool compare
    )
    {
        if (Capture)
            return;
        _plan = plan;
        _zone = plan.TimeZone;
        _monitor = monitor;
        _others = others.Where(m => m.Id != monitor.Id).ToArray();
        _date = date;
        _selected = selected;
        _brightness = brightness;
        _compare = compare;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_monitor is null)
            return;
        _nodes = ScheduleTimeline.ResolveDay(_plan, _monitor, _date);
        _events = ScheduleTimeline.Build(_plan, _monitor, _date);
        Invalidate();
    }

    private float UiScale => DeviceDpi / 96f;
    private RectangleF Plot =>
        new(
            60 * UiScale,
            34 * UiScale,
            Math.Max(1, Width - 83 * UiScale),
            Math.Max(1, Height - 87 * UiScale)
        );
    private double Maximum =>
        _brightness
            ? 100
            : Math.Max(
                7000,
                Math.Ceiling(
                    _monitor
                        ?.PointColors.Values.Select(p => p.Temperature)
                        .DefaultIfEmpty(7000)
                        .Max() / 1000
                        ?? 7
                ) * 1000
            );
    private double Minimum => _brightness ? 0 : 500;

    private float X(double minute) => Plot.Left + (float)(minute / 1440) * Plot.Width;

    private double Minute(float x) => Math.Clamp((x - Plot.Left) / Plot.Width * 1440, 0, 1439);

    private float Y(double value) =>
        Plot.Bottom - (float)((value - Minimum) / (Maximum - Minimum)) * Plot.Height;

    private double Value(float y) =>
        Math.Clamp(
            Minimum + (Plot.Bottom - y) / Plot.Height * (Maximum - Minimum),
            _brightness ? 1 : 500,
            Maximum
        );

    private double Amount(ColorProfile color) =>
        _brightness ? color.Brightness * 100
        : color.Mode == ColorMode.PureRed ? 500
        : color.Temperature;

    private double LocalMinute(DateTimeOffset time) =>
        time.Hour * 60 + time.Minute + time.Second / 60d;

    private DateTimeOffset Instant(double minute) =>
        SolarTimes.AtLocalTime(
            _date,
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(minute, 0, 1439.99))),
            _zone
        );

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_monitor is null || Width < 100 || Height < 100)
            return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = Plot;
        using var small = new Font("Segoe UI", 9);
        using var muted = new SolidBrush(LightTheme.Muted);
        using var text = new SolidBrush(LightTheme.Text);
        using var grid = new Pen(Color.FromArgb(45, LightTheme.Line), UiScale);
        var solar = _plan.Location is null ? null : SolarTimes.Calculate(_date, _plan.Location);
        if (solar?.Sunrise is { } sunrise && solar.Sunset is { } sunset)
        {
            using var day = new SolidBrush(Color.FromArgb(12, LightTheme.Accent));
            g.FillRectangle(
                day,
                X(LocalMinute(sunrise)),
                plot.Top,
                X(LocalMinute(sunset)) - X(LocalMinute(sunrise)),
                plot.Height
            );
            DrawSun(g, sunrise, "Nascer", small);
            DrawSun(g, sunset, "Pôr", small);
        }
        for (var h = 0; h <= 24; h += 3)
        {
            var x = X(h * 60);
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            var label = h.ToString("00");
            var size = g.MeasureString(label, small);
            g.DrawString(label, small, muted, x - size.Width / 2, plot.Bottom + 13 * UiScale);
        }
        var ticks = _brightness
            ? new[] { 0d, 25, 50, 75, 100 }
            : new[] { 500d, 2000, 4000, Maximum };
        foreach (var tick in ticks.Distinct())
        {
            g.DrawLine(grid, plot.Left, Y(tick), plot.Right, Y(tick));
            var label = _brightness ? $"{tick:0}%" : $"{tick:0}";
            var size = g.MeasureString(label, small);
            g.DrawString(
                label,
                small,
                muted,
                plot.Left - size.Width - 12 * UiScale,
                Y(tick) - size.Height / 2
            );
        }
        var clipping = g.Save();
        g.SetClip(RectangleF.Inflate(plot, 1, 1));
        if (_compare)
            foreach (var other in _others)
                DrawCurve(
                    g,
                    ScheduleTimeline.Build(_plan, other, _date),
                    other.Manual,
                    LightTheme.Muted,
                    true
                );
        DrawCurve(
            g,
            _events,
            _monitor.Manual,
            _brightness ? LightTheme.Copper : LightTheme.Accent,
            false
        );
        g.Restore(clipping);
        foreach (var node in _nodes)
        {
            var profile = ScheduleTimeline.Color(_monitor, node.Point);
            var pos = new PointF(X(LocalMinute(node.Time)), Y(Amount(profile)));
            var actual = ScheduleTimeline.Evaluate(_events, node.Time, _monitor.Manual);
            var selected = node.Point.Id == _selected;
            var color = _brightness ? LightTheme.Copper : LightTheme.Accent;
            using var connector = new Pen(Color.FromArgb(105, color), UiScale)
            {
                DashStyle = DashStyle.Dot,
            };
            g.DrawLine(connector, pos, new(pos.X, Y(Amount(actual))));
            using var outer = new SolidBrush(Color.FromArgb(selected ? 40 : 20, color));
            var halo = (selected ? 11 : 8) * UiScale;
            g.FillEllipse(outer, pos.X - halo, pos.Y - halo, halo * 2, halo * 2);
            using var fill = new SolidBrush(selected ? color : LightTheme.Background);
            using var outline = new Pen(color, 2 * UiScale);
            var radius = 5 * UiScale;
            g.FillEllipse(fill, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
            g.DrawEllipse(outline, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
            if (selected)
            {
                var duration = node.Point.TransitionMinutes ?? _plan.TransitionMinutes;
                var endMinute = LocalMinute(node.Time) + duration;
                if (duration > 0 && endMinute < 1440)
                {
                    using var durationPen = new Pen(Color.FromArgb(130, color), UiScale)
                    {
                        DashStyle = DashStyle.Dash,
                    };
                    var end = new PointF(X(endMinute), pos.Y);
                    g.DrawLine(durationPen, pos, end);
                    g.DrawLine(durationPen, end.X, end.Y - 5 * UiScale, end.X, end.Y + 5 * UiScale);
                }
                var label =
                    profile.Mode == ColorMode.PureRed && !_brightness ? "Vermelho"
                    : _brightness ? $"{profile.Brightness:P0}"
                    : $"{profile.Temperature:0} K";
                var size = g.MeasureString(label, small);
                var labelY =
                    pos.Y < plot.Top + 30 * UiScale ? pos.Y + 14 * UiScale : pos.Y - 30 * UiScale;
                var box = new RectangleF(
                    Math.Clamp(
                        pos.X - size.Width / 2 - 5 * UiScale,
                        plot.Left,
                        Math.Max(plot.Left, plot.Right - size.Width - 10 * UiScale)
                    ),
                    labelY,
                    size.Width + 10 * UiScale,
                    size.Height + 3 * UiScale
                );
                using var labelBackground = new SolidBrush(LightTheme.Field);
                g.FillRectangle(labelBackground, box);
                g.DrawString(label, small, text, box.X + 5 * UiScale, box.Y);
            }
        }
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, _plan.TimeZone);
        if (DateOnly.FromDateTime(now.DateTime) == _date)
        {
            var x = X(LocalMinute(now));
            using var nowPen = new Pen(Color.FromArgb(110, LightTheme.Text), UiScale)
            {
                DashStyle = DashStyle.Dash,
            };
            g.DrawLine(nowPen, x, plot.Top, x, plot.Bottom);
            g.DrawString(
                "Agora",
                small,
                muted,
                Math.Min(x + 5 * UiScale, plot.Right - 40 * UiScale),
                Height - 22 * UiScale
            );
        }
        if (_hoverX >= plot.Left && _hoverX <= plot.Right && !Capture && !_staticPreview)
        {
            var instant = Instant(Minute(_hoverX));
            var color = ScheduleTimeline.Evaluate(_events, instant, _monitor.Manual);
            using var hover = new Pen(Color.FromArgb(65, LightTheme.Text), UiScale);
            g.DrawLine(hover, _hoverX, plot.Top, _hoverX, plot.Bottom);
            var label =
                $"{instant:HH:mm}   {(color.Mode == ColorMode.PureRed ? "Vermelho" : $"{color.Temperature:0} K")}   {color.Brightness:P0}";
            var size = g.MeasureString(label, small);
            using var background = new SolidBrush(LightTheme.Field);
            var x = Math.Clamp(
                _hoverX - size.Width / 2,
                plot.Left,
                Math.Max(plot.Left, plot.Right - size.Width - 12 * UiScale)
            );
            g.FillRectangle(background, x, 4 * UiScale, size.Width + 12 * UiScale, 25 * UiScale);
            g.DrawString(label, small, text, x + 6 * UiScale, 7 * UiScale);
        }
        if (Focused)
            ControlPaint.DrawFocusRectangle(
                g,
                Rectangle.Inflate(ClientRectangle, -2, -2),
                LightTheme.Line,
                BackColor
            );
    }

    private void DrawSun(Graphics g, DateTimeOffset time, string name, Font font)
    {
        var x = X(LocalMinute(time));
        using var pen = new Pen(Color.FromArgb(90, LightTheme.Accent), UiScale)
        {
            DashStyle = DashStyle.Dot,
        };
        using var brush = new SolidBrush(LightTheme.Muted);
        g.DrawLine(pen, x, Plot.Top, x, Plot.Bottom);
        var label = $"{name} {time:HH:mm}";
        var width = g.MeasureString(label, font).Width;
        g.DrawString(
            label,
            font,
            brush,
            Math.Clamp(x - width / 2, Plot.Left, Plot.Right - width),
            8 * UiScale
        );
    }

    private void DrawCurve(
        Graphics g,
        ScheduledColorEvent[] events,
        ColorProfile fallback,
        Color color,
        bool secondary
    )
    {
        var points = Enumerable
            .Range(0, 481)
            .Select(i => new PointF(
                X(i * 3),
                Y(Amount(ScheduleTimeline.Evaluate(events, Instant(i * 3), fallback)))
            ))
            .ToArray();
        using var pen = new Pen(
            secondary ? Color.FromArgb(75, color) : color,
            (secondary ? 1.2f : 2.5f) * UiScale
        );
        if (secondary)
            pen.DashStyle = DashStyle.Dash;
        else
        {
            using var path = new GraphicsPath();
            path.AddLines(points);
            path.AddLine(points[^1], new(Plot.Right, Plot.Bottom));
            path.AddLine(new PointF(Plot.Right, Plot.Bottom), new(Plot.Left, Plot.Bottom));
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(17, color));
            g.FillPath(fill, path);
        }
        g.DrawLines(pen, points);
    }

    private ResolvedTiming? Hit(Point location) =>
        _monitor is null
            ? null
            : _nodes
                .Select(n =>
                    (
                        Node: n,
                        Distance: Math.Pow(X(LocalMinute(n.Time)) - location.X, 2)
                            + Math.Pow(
                                Y(Amount(ScheduleTimeline.Color(_monitor, n.Point))) - location.Y,
                                2
                            )
                    )
                )
                .Where(n => n.Distance <= Math.Pow(22 * UiScale, 2))
                .OrderBy(n => n.Distance)
                .Select(n => n.Node)
                .FirstOrDefault();

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || _monitor is null)
            return;
        var hit = Hit(e.Location);
        if (hit is null)
            return;
        _selected = hit.Point.Id;
        PointSelected?.Invoke(_selected);
        _dragPoint = hit.Point;
        _dragColor = ScheduleTimeline.Color(_monitor, hit.Point);
        _dragStart = e.Location;
        _moved = false;
        Capture = true;
        Cursor = Cursors.SizeAll;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hoverX = e.X;
        if (Capture && _dragPoint is not null && _monitor is not null)
        {
            if (
                !_moved
                && Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) < 4 * UiScale
            )
                return;
            _moved = true;
            var minute = Math.Clamp((int)Math.Round(Minute(e.X) / 5) * 5, 0, 1439);
            var point = MoveTime(_dragPoint, minute);
            var value = Value(e.Y);
            var color =
                _brightness ? _dragColor! with { Brightness = Math.Round(value) / 100 }
                : _dragColor!.Mode == ColorMode.PureRed ? _dragColor
                : _dragColor with
                {
                    Temperature = Math.Clamp(Math.Round(value / 50) * 50, 500, 12000),
                    Mode = ColorMode.Temperature,
                };
            ApplyDraft(point, color);
        }
        else
            Cursor = Hit(e.Location) is null ? Cursors.Cross : Cursors.SizeAll;
        Invalidate();
    }

    private TimingPoint MoveTime(TimingPoint point, int minute)
    {
        var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
        if (point.Anchor == TimeAnchor.Clock || _plan.Location is null)
            return point with { Time = time };
        var sun = SolarTimes.Calculate(_date, _plan.Location);
        var instant = point.Anchor == TimeAnchor.Sunrise ? sun.Sunrise : sun.Sunset;
        return instant is null
            ? point with
            {
                Time = time,
            }
            : point with
            {
                OffsetMinutes = Math.Clamp(minute - (int)LocalMinute(instant.Value), -720, 720),
            };
    }

    private void ApplyDraft(TimingPoint point, ColorProfile color)
    {
        if (_monitor is null)
            return;
        if (_monitor.UseSharedSchedule)
            _plan = _plan with
            {
                Points = _plan.Points.Select(p => p.Id == point.Id ? point : p).ToList(),
            };
        else
            _monitor = _monitor with
            {
                OwnTiming = _monitor.OwnTiming.Select(p => p.Id == point.Id ? point : p).ToList(),
            };
        _monitor = _monitor with { PointColors = new(_monitor.PointColors) { [point.Id] = color } };
        Rebuild();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!Capture || _dragPoint is null || _monitor is null)
            return;
        var point = ScheduleTimeline.Points(_plan, _monitor).Single(p => p.Id == _dragPoint.Id);
        var color = ScheduleTimeline.Color(_monitor, point);
        var moved = _moved;
        _dragPoint = null;
        _dragColor = null;
        Capture = false;
        Cursor = Cursors.Default;
        if (moved)
            PointEdited?.Invoke(point, color);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || !Plot.Contains(e.Location))
            return;
        var hit = Hit(e.Location);
        if (hit is not null)
        {
            CancelDrag();
            _selected = hit.Point.Id;
            PointSelected?.Invoke(_selected);
            CurveRequested?.Invoke();
            return;
        }
        var instant = Instant(Minute(e.X));
        var transition = _nodes.LastOrDefault(n =>
            n.Time <= instant
            && instant <= n.Time.AddMinutes(n.Point.TransitionMinutes ?? _plan.TransitionMinutes)
        );
        if (
            transition is not null
            && _monitor is not null
            && Math.Abs(
                e.Y - Y(Amount(ScheduleTimeline.Evaluate(_events, instant, _monitor.Manual)))
            )
                < 18 * UiScale
        )
        {
            _selected = transition.Point.Id;
            PointSelected?.Invoke(_selected);
            CurveRequested?.Invoke();
            return;
        }
        if (Hit(e.Location) is null)
            PointAdded?.Invoke(
                TimeOnly.FromTimeSpan(
                    TimeSpan.FromMinutes(Math.Clamp((int)Math.Round(Minute(e.X) / 5) * 5, 0, 1439))
                ),
                Value(e.Y)
            );
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = -1;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && _dragPoint is not null)
            CancelDrag();
    }

    private void CancelDrag()
    {
        if (_dragPoint is null)
            return;
        ApplyDraft(_dragPoint, _dragColor!);
        _dragPoint = null;
        _dragColor = null;
        _moved = false;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Escape
        || base.IsInputKey(keyData);

    internal void ClearHover()
    {
        _hoverX = -1;
        Invalidate();
    }

    internal void StaticPreview()
    {
        _staticPreview = true;
        ClearHover();
    }

    internal void ExerciseDrag(
        string id,
        int minute,
        double value,
        Action duringDraft,
        bool cancel = false
    )
    {
        var node = _nodes.Single(n => n.Point.Id == id);
        var start = new Point(
            (int)Math.Round(X(LocalMinute(node.Time))),
            (int)Math.Round(Y(Amount(ScheduleTimeline.Color(_monitor!, node.Point))))
        );
        var target = new Point((int)Math.Round(X(minute)), (int)Math.Round(Y(value)));
        OnMouseDown(new(MouseButtons.Left, 1, start.X, start.Y, 0));
        OnMouseMove(new(MouseButtons.Left, 0, target.X, target.Y, 0));
        duringDraft();
        if (cancel)
            OnKeyDown(new(Keys.Escape));
        OnMouseUp(new(MouseButtons.Left, 1, target.X, target.Y, 0));
        ClearHover();
    }

    internal void ExerciseKey(Keys key) => OnKeyDown(new(key));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_monitor is null)
            return;
        if (e.KeyCode == Keys.Escape && _dragPoint is not null)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }
        var node = _nodes.FirstOrDefault(p => p.Point.Id == _selected);
        if (node is null)
            return;
        if (e.KeyCode == Keys.Delete)
        {
            PointRemoved?.Invoke(node.Point.Id);
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Enter)
        {
            EditRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down))
            return;
        var color = ScheduleTimeline.Color(_monitor, node.Point);
        var point = node.Point;
        var direction = e.KeyCode is Keys.Left or Keys.Down ? -1 : 1;
        if (e.KeyCode is Keys.Left or Keys.Right)
            point = MoveTime(
                point,
                Math.Clamp((int)LocalMinute(node.Time) + direction * (e.Shift ? 1 : 5), 0, 1439)
            );
        else if (_brightness)
            color = color with
            {
                Brightness = Math.Clamp(color.Brightness + direction * .01, .01, 1),
            };
        else if (color.Mode != ColorMode.PureRed)
            color = color with
            {
                Mode = ColorMode.Temperature,
                Temperature = Math.Clamp(color.Temperature + direction * 50, 500, 12000),
            };
        PointEdited?.Invoke(point, color);
        e.Handled = true;
    }
}
