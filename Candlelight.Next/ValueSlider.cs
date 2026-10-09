namespace Candlelight.Next;

internal sealed class ValueSlider : Control
{
    private int _value;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal int Minimum { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal int Maximum { get; set; } = 100;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal int Step { get; set; } = 1;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (_value == value)
                return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;

    public ValueSlider()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable,
            true
        );
        Height = 34;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var left = 9;
        var right = Math.Max(left + 1, Width - 9);
        var x = left + (float)(Value - Minimum) / Math.Max(1, Maximum - Minimum) * (right - left);
        using var line = new Pen(Color.FromArgb(64, 76, 75), 4);
        using var active = new Pen(Color.FromArgb(214, 178, 120), 4);
        using var thumb = new SolidBrush(
            Enabled ? Color.FromArgb(233, 200, 146) : Color.FromArgb(85, 94, 91)
        );
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLine(line, left, Height / 2f, right, Height / 2f);
        if (Enabled)
            e.Graphics.DrawLine(active, left, Height / 2f, x, Height / 2f);
        e.Graphics.FillEllipse(thumb, x - 7, Height / 2f - 7, 14, 14);
        if (Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
            SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    private void SetFromX(int x)
    {
        if (!Enabled)
            return;
        var value = Minimum + (x - 9d) / Math.Max(1, Width - 18) * (Maximum - Minimum);
        Value = (int)(Math.Round(value / Step) * Step);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Down)
            Value -= Step;
        if (e.KeyCode is Keys.Right or Keys.Up)
            Value += Step;
        if (e.KeyCode == Keys.Home)
            Value = Minimum;
        if (e.KeyCode == Keys.End)
            Value = Maximum;
    }
}
