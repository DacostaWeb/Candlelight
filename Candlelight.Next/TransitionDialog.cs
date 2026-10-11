using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class TransitionDialog : Form
{
    private readonly CheckBox _defaultDuration = new()
    {
        Text = "Usar duração global",
        AutoSize = true,
    };
    private readonly CheckBox _defaultCurve = new() { Text = "Usar curva global", AutoSize = true };
    private readonly NumericUpDown _duration = new()
    {
        Minimum = 0,
        Maximum = 1440,
        Increment = 5,
        Width = 76,
    };
    private readonly CurveFields _curve = new();
    private readonly TimingPoint _point;
    internal TimingPoint Result =>
        _point with
        {
            TransitionMinutes = _defaultDuration.Checked ? null : (int)_duration.Value,
            Curve = _defaultCurve.Checked ? null : _curve.Curve,
        };

    internal TransitionDialog(TimingPoint point, SchedulePlan plan, string scope)
    {
        _point = point;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Moldar transição";
        BackColor = LightTheme.Background;
        ForeColor = LightTheme.Text;
        Font = new("Segoe UI", 10);
        ClientSize = new(720, 550);
        MinimumSize = new(680, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        Icon = new Icon(Path.Combine(System.AppContext.BaseDirectory, "favicon.ico"));
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(24, 18, 24, 16),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var size in new[] { 42, 30, 38, 32, -1, 42 })
            root.RowStyles.Add(
                new(size < 0 ? SizeType.Percent : SizeType.Absolute, size < 0 ? 100 : size)
            );
        root.Controls.Add(LightTheme.Label("A mudança ao teu ritmo", 18), 0, 0);
        root.Controls.Add(
            LightTheme.Label(
                scope + " · Duração e curva acompanham os horários partilhados.",
                9,
                true
            ),
            0,
            1
        );
        var duration = LightTheme.Row();
        duration.Controls.Add(_defaultDuration);
        duration.Controls.Add(_duration);
        duration.Controls.Add(LightTheme.Label("minutos"));
        root.Controls.Add(duration, 0, 2);
        root.Controls.Add(_defaultCurve, 0, 3);
        root.Controls.Add(_curve, 0, 4);
        var buttons = LightTheme.Row();
        buttons.FlowDirection = FlowDirection.RightToLeft;
        var apply = LightTheme.Button("Aplicar curva", () => DialogResult = DialogResult.OK);
        var cancel = LightTheme.Button("Cancelar", () => DialogResult = DialogResult.Cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = cancel;
        _duration.Value = point.TransitionMinutes ?? plan.TransitionMinutes;
        _curve.Curve =
            point.Curve
            ?? (plan.SmoothTransitions ? TransitionCurve.Smooth : TransitionCurve.Linear);
        _defaultDuration.Checked = point.TransitionMinutes is null;
        _defaultCurve.Checked = point.Curve is null;
        _duration.Enabled = !_defaultDuration.Checked;
        _curve.Enabled = !_defaultCurve.Checked;
        _defaultDuration.CheckedChanged += (_, _) => _duration.Enabled = !_defaultDuration.Checked;
        _defaultCurve.CheckedChanged += (_, _) => _curve.Enabled = !_defaultCurve.Checked;
        LightTheme.Style(this);
        AutoScaleDimensions = new(96, 96);
    }

    internal void VerifyInteraction()
    {
        _defaultDuration.Checked = false;
        _duration.Value = 90;
        _defaultCurve.Checked = false;
        _curve.VerifyInteraction();
        if (Result.TransitionMinutes != 90 || Result.Curve is null)
            throw new InvalidOperationException("Transition dialog binding failed.");
    }
}
