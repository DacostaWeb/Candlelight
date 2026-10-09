namespace Candlelight.Next;

internal sealed class DarkComboBox : ComboBox
{
    public DarkComboBox() =>
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
            true
        );

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(Color.FromArgb(66, 80, 76));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(6, 0, Math.Max(1, Width - 30), Height),
            ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
        );
        using var arrow = new SolidBrush(ForeColor);
        var x = Width - 15;
        var y = Height / 2;
        e.Graphics.FillPolygon(
            arrow,
            [new Point(x - 4, y - 2), new Point(x + 4, y - 2), new Point(x, y + 3)]
        );
        if (Focused)
            ControlPaint.DrawFocusRectangle(
                e.Graphics,
                new(3, 3, Width - 23, Height - 6),
                ForeColor,
                BackColor
            );
    }
}
