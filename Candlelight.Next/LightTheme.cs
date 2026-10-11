using System.Drawing.Drawing2D;

namespace Candlelight.Next;

internal static class LightTheme
{
    internal static readonly Color Background = ColorTranslator.FromHtml("#181E23");
    internal static readonly Color Surface = ColorTranslator.FromHtml("#222C33");
    internal static readonly Color Field = ColorTranslator.FromHtml("#2C3941");
    internal static readonly Color Text = ColorTranslator.FromHtml("#E9E0D1");
    internal static readonly Color Muted = ColorTranslator.FromHtml("#A8B6B9");
    internal static readonly Color Accent = ColorTranslator.FromHtml("#E6BC78");
    internal static readonly Color Copper = ColorTranslator.FromHtml("#D39B85");
    internal static readonly Color Line = ColorTranslator.FromHtml("#41515A");

    internal static Button Button(string text, Action click, bool accent = false)
    {
        var button = new ActionButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Padding = new(0),
            Margin = new(0, 0, 8, 0),
            BackColor = accent ? Accent : Field,
            ForeColor = accent ? Background : Text,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = accent
            ? Color.FromArgb(244, 205, 145)
            : Color.FromArgb(59, 75, 84);
        button.Click += (_, _) => click();
        return button;
    }

    internal static Label Label(string text, float size = 10, bool muted = false) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = muted ? Muted : Text,
            Font = new Font("Segoe UI", size),
            Margin = new(0, 5, 8, 0),
        };

    internal static FlowLayoutPanel Row() =>
        new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new(0),
            Padding = new(0),
        };

    internal static void Style(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TextBox or ComboBox or NumericUpDown or ListBox)
            {
                control.BackColor = Field;
                control.ForeColor = Text;
            }
            if (control is ComboBox combo)
            {
                combo.FlatStyle = FlatStyle.Flat;
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.ItemHeight = Math.Max(
                    (int)(24 * combo.DeviceDpi / 96f),
                    combo.Font.Height + 6
                );
                combo.DrawItem += (_, e) =>
                {
                    using var background = new SolidBrush(
                        (e.State & DrawItemState.Selected) != 0 ? Line : Field
                    );
                    e.Graphics.FillRectangle(background, e.Bounds);
                    if (e.Index >= 0)
                        TextRenderer.DrawText(
                            e.Graphics,
                            combo.GetItemText(combo.Items[e.Index]),
                            e.Font,
                            e.Bounds,
                            Text,
                            TextFormatFlags.VerticalCenter
                                | TextFormatFlags.Left
                                | TextFormatFlags.EndEllipsis
                        );
                    if ((e.State & DrawItemState.Focus) != 0)
                        e.DrawFocusRectangle();
                };
            }
            Style(control);
        }
    }
}

internal sealed class ActionButton : Button
{
    private bool _hovered;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = Enabled && _hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        e.Graphics.Clear(background);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            Enabled ? ForeColor : LightTheme.Muted,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
        );
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(
                e.Graphics,
                Rectangle.Inflate(ClientRectangle, -3, -3),
                LightTheme.Accent,
                background
            );
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var text = TextRenderer.MeasureText(
            Text,
            Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
        );
        return new(
            (int)Math.Ceiling(Math.Max(32 * scale, text.Width + 24 * scale)),
            (int)Math.Ceiling(Math.Max(32 * scale, Font.Height + 8 * scale))
        );
    }
}

internal sealed class MonitorTab : Button
{
    private readonly string _name;
    private readonly bool _selected;
    private readonly string _detail;

    internal MonitorTab(string name, string detail, bool selected, Action click)
    {
        _name = name;
        _detail = detail;
        _selected = selected;
        Width = 218;
        Height = 49;
        Margin = new(0, 0, 10, 0);
        AccessibleName = name + ". " + detail;
        Text = name;
        Cursor = Cursors.Hand;
        Click += (_, _) => click();
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint,
            true
        );
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        var width = Width / scale;
        using var fill = new SolidBrush(_selected ? LightTheme.Field : LightTheme.Background);
        g.FillRectangle(fill, 0, 0, width, 49);
        using var pen = new Pen(_selected ? LightTheme.Accent : LightTheme.Muted, 1.5f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawRectangle(pen, 12, 13, 25, 17);
        g.DrawLine(pen, 24, 30, 24, 35);
        g.DrawLine(pen, 18, 35, 30, 35);
        using var nameFont = new Font("Segoe UI", 13.3f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 11.3f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new SolidBrush(LightTheme.Text);
        using var muted = new SolidBrush(LightTheme.Muted);
        g.DrawString(_name, nameFont, text, new RectangleF(47, 5, width - 50, 22));
        g.DrawString(_detail, smallFont, muted, new RectangleF(47, 27, width - 50, 18));
        if (_selected)
            g.DrawLine(pen, 0, 48, width, 48);
        g.ResetTransform();
        if (Focused)
            ControlPaint.DrawFocusRectangle(g, ClientRectangle, LightTheme.Accent, BackColor);
    }
}
