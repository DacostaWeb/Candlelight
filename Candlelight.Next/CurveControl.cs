using System.Drawing.Drawing2D;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class CurveControl : Control
{
    private TransitionCurve _curve = TransitionCurve.Smooth;
    private TransitionCurve? _beforeDrag;
    private int _handle;
    internal event Action? Changed;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal TransitionCurve Curve
    {
        get => _curve;
        set
        {
            value.Validate();
            _curve = value;
            Invalidate();
        }
    }
    private float ScaleFactor => DeviceDpi / 96f;
    private RectangleF Plot =>
        new(
            44 * ScaleFactor,
            16 * ScaleFactor,
            Math.Max(1, Width - 70 * ScaleFactor),
            Math.Max(1, Height - 54 * ScaleFactor)
        );

    private PointF At(double x, double y) =>
        new(Plot.Left + (float)x * Plot.Width, Plot.Bottom - (float)y * Plot.Height);

    internal CurveControl()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable,
            true
        );
        Dock = DockStyle.Fill;
        TabStop = true;
        ResizeRedraw = true;
        AccessibleName =
            "Curva de transição. Arrasta os dois controlos; Espaço seleciona o outro; setas ajustam; Escape cancela.";
        AccessibleRole = AccessibleRole.Diagram;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(55, LightTheme.Line), ScaleFactor);
        using var muted = new SolidBrush(LightTheme.Muted);
        using var text = new SolidBrush(LightTheme.Text);
        using var font = new Font("Segoe UI", 9);
        for (var i = 0; i <= 4; i++)
        {
            var fraction = i / 4d;
            g.DrawLine(grid, At(fraction, 0), At(fraction, 1));
            g.DrawLine(grid, At(0, fraction), At(1, fraction));
        }
        g.DrawString("Início", font, muted, Plot.Left, Plot.Bottom + 10 * ScaleFactor);
        var end = g.MeasureString("Fim", font);
        g.DrawString("Fim", font, muted, Plot.Right - end.Width, Plot.Bottom + 10 * ScaleFactor);
        g.DrawString("100%", font, muted, 0, Plot.Top);
        g.DrawString("0%", font, muted, 12 * ScaleFactor, Plot.Bottom - font.Height);
        using var diagonal = new Pen(LightTheme.Line, ScaleFactor) { DashStyle = DashStyle.Dash };
        g.DrawLine(diagonal, At(0, 0), At(1, 1));
        var points = Enumerable
            .Range(0, 201)
            .Select(i => At(i / 200d, _curve.Evaluate(i / 200d)))
            .ToArray();
        using var curve = new Pen(Enabled ? LightTheme.Accent : LightTheme.Muted, 3 * ScaleFactor);
        g.DrawLines(curve, points);
        var handles = new[] { At(_curve.X1, _curve.Y1), At(_curve.X2, _curve.Y2) };
        using var stem = new Pen(LightTheme.Copper, ScaleFactor);
        g.DrawLine(stem, At(0, 0), handles[0]);
        g.DrawLine(stem, At(1, 1), handles[1]);
        for (var i = 0; i < 2; i++)
        {
            using var fill = new SolidBrush(
                i == _handle && Focused ? LightTheme.Accent : LightTheme.Field
            );
            var h = handles[i];
            var r = 8 * ScaleFactor;
            g.FillEllipse(fill, h.X - r, h.Y - r, r * 2, r * 2);
            g.DrawEllipse(stem, h.X - r, h.Y - r, r * 2, r * 2);
            var label = (i + 1).ToString();
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, text, h.X - size.Width / 2, h.Y - size.Height / 2);
        }
        if (Focused)
            ControlPaint.DrawFocusRectangle(
                g,
                Rectangle.Inflate(ClientRectangle, -2, -2),
                LightTheme.Line,
                BackColor
            );
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        var first = At(_curve.X1, _curve.Y1);
        var second = At(_curve.X2, _curve.Y2);
        static double Distance(PointF p, Point q) =>
            Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2);
        _handle = Distance(first, e.Location) <= Distance(second, e.Location) ? 0 : 1;
        if (
            Math.Min(Distance(first, e.Location), Distance(second, e.Location))
            > Math.Pow(24 * ScaleFactor, 2)
        )
            return;
        _beforeDrag = _curve;
        Capture = true;
        Invalidate();
    }

    private void MoveHandle(double x, double y)
    {
        Curve =
            _handle == 0
                ? _curve with
                {
                    X1 = Math.Clamp(x, 0, _curve.X2),
                    Y1 = Math.Clamp(y, 0, _curve.Y2),
                }
                : _curve with
                {
                    X2 = Math.Clamp(x, _curve.X1, 1),
                    Y2 = Math.Clamp(y, _curve.Y1, 1),
                };
        Changed?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture)
            MoveHandle((e.X - Plot.Left) / Plot.Width, (Plot.Bottom - e.Y) / Plot.Height);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _beforeDrag = null;
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && _beforeDrag is { } before)
        {
            _beforeDrag = null;
            Curve = before;
            Changed?.Invoke();
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode)
            is Keys.Left
                or Keys.Right
                or Keys.Up
                or Keys.Down
                or Keys.Space
                or Keys.Escape
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && Capture)
        {
            Capture = false;
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Space)
        {
            _handle = 1 - _handle;
            Invalidate();
            e.Handled = true;
            return;
        }
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down))
            return;
        var x = _handle == 0 ? _curve.X1 : _curve.X2;
        var y = _handle == 0 ? _curve.Y1 : _curve.Y2;
        var step = e.Shift ? .01 : .05;
        MoveHandle(
            x
                + (
                    e.KeyCode == Keys.Left ? -step
                    : e.KeyCode == Keys.Right ? step
                    : 0
                ),
            y
                + (
                    e.KeyCode == Keys.Down ? -step
                    : e.KeyCode == Keys.Up ? step
                    : 0
                )
        );
        e.Handled = true;
    }

    internal void ExerciseDrag(double x, double y, bool cancel)
    {
        var start = At(_curve.X1, _curve.Y1);
        var target = At(x, y);
        OnMouseDown(new(MouseButtons.Left, 1, (int)start.X, (int)start.Y, 0));
        OnMouseMove(new(MouseButtons.Left, 0, (int)target.X, (int)target.Y, 0));
        if (cancel)
            OnKeyDown(new(Keys.Escape));
        OnMouseUp(new(MouseButtons.Left, 1, (int)target.X, (int)target.Y, 0));
    }
}

internal sealed class CurveFields : UserControl
{
    private readonly CurveControl _graph = new();
    private readonly NumericUpDown[] _values = Enumerable
        .Range(0, 4)
        .Select(_ => new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Width = 62,
            DecimalPlaces = 1,
            Increment = 1,
        })
        .ToArray();
    private bool _loading;
    internal event Action? Changed;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal TransitionCurve Curve
    {
        get => _graph.Curve;
        set
        {
            _graph.Curve = value;
            Synchronize();
        }
    }

    internal CurveFields()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
        Margin = new(0);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new(0),
        };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 38));
        layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 28));
        layout.Controls.Add(_graph, 0, 0);
        var presets = LightTheme.Row();
        foreach (
            var item in new[]
            {
                ("Suave", TransitionCurve.Smooth),
                ("Linear", TransitionCurve.Linear),
                ("Começa devagar", TransitionCurve.SlowStart),
                ("Termina devagar", TransitionCurve.SlowEnd),
            }
        )
        {
            var preset = item;
            presets.Controls.Add(
                LightTheme.Button(
                    preset.Item1,
                    () =>
                    {
                        Curve = preset.Item2;
                        Changed?.Invoke();
                    }
                )
            );
        }
        layout.Controls.Add(presets, 0, 1);
        var precise = LightTheme.Row();
        for (var i = 0; i < 4; i++)
        {
            precise.Controls.Add(
                LightTheme.Label(i % 2 == 0 ? $"{i / 2 + 1} · Tempo %" : "Mudança %")
            );
            _values[i].AccessibleName =
                $"Controlo {i / 2 + 1}: {(i % 2 == 0 ? "tempo" : "mudança")} em percentagem";
            precise.Controls.Add(_values[i]);
            _values[i].ValueChanged += (_, _) =>
            {
                if (_loading)
                    return;
                var a = (double)_values[0].Value / 100;
                var b = (double)_values[1].Value / 100;
                var c = Math.Max(a, (double)_values[2].Value / 100);
                var d = Math.Max(b, (double)_values[3].Value / 100);
                Curve = new(a, b, c, d);
                Changed?.Invoke();
            };
        }
        layout.Controls.Add(precise, 0, 2);
        layout.Controls.Add(
            LightTheme.Label(
                "Arrasta 1 e 2 para moldar a curva. As setas permitem ajustes finos.",
                9,
                true
            ),
            0,
            3
        );
        Controls.Add(layout);
        _graph.Changed += () =>
        {
            Synchronize();
            Changed?.Invoke();
        };
        Synchronize();
    }

    private void Synchronize()
    {
        _loading = true;
        var curve = _graph.Curve;
        var values = new[] { curve.X1, curve.Y1, curve.X2, curve.Y2 };
        for (var i = 0; i < 4; i++)
            _values[i].Value = (decimal)Math.Round(values[i] * 100, 1);
        _loading = false;
    }

    internal void VerifyInteraction()
    {
        var before = Curve;
        _graph.ExerciseDrag(.15, .1, true);
        if (Curve != before)
            throw new InvalidOperationException("Escape changed the curve draft.");
        _graph.ExerciseDrag(.15, .1, false);
        if (Math.Abs(Curve.X1 - .15) > .02 || Math.Abs(Curve.Y1 - .1) > .02)
            throw new InvalidOperationException("Curve drag did not update its controls.");
        _values[0].Value = 25;
        if (Curve.X1 != .25)
            throw new InvalidOperationException("Curve precision control failed.");
        Curve = before;
    }
}
