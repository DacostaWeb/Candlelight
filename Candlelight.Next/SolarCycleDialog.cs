using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class SolarCycleDialog : Form
{
    private readonly SchedulePlan _plan;
    private readonly DateOnly _date;
    private readonly CurveFields _curve = new();
    private readonly NumericUpDown _offset = new()
    {
        Minimum = -720,
        Maximum = 720,
        Increment = 5,
        Width = 75,
    };
    private readonly NumericUpDown _duration = new()
    {
        Minimum = 0,
        Maximum = 1440,
        Increment = 5,
        Width = 75,
    };
    private readonly Label _summary = LightTheme.Label("", 10);
    private readonly Label _description = LightTheme.Label("", 9, true);
    private readonly Button _morning;
    private readonly Button _evening;
    private readonly TimingPoint[] _points;
    private int _selected;
    private bool _loading;
    internal TimingPoint Morning => _points[0];
    internal TimingPoint Evening => _points[1];

    internal SolarCycleDialog(
        SchedulePlan plan,
        MonitorProfile monitor,
        DateOnly date,
        string scope
    )
    {
        _plan = plan;
        _date = date;
        var existing = ScheduleTimeline.Points(plan, monitor);
        _points =
        [
            existing.FirstOrDefault(p => p.Anchor == TimeAnchor.Sunrise)
                ?? new(
                    "solar-morning",
                    new(7, 0),
                    TimeAnchor.Sunrise,
                    -30,
                    90,
                    TransitionCurve.Smooth
                ),
            existing.FirstOrDefault(p => p.Anchor == TimeAnchor.Sunset)
                ?? new(
                    "solar-evening",
                    new(18, 0),
                    TimeAnchor.Sunset,
                    -60,
                    120,
                    TransitionCurve.Smooth
                ),
        ];
        for (var i = 0; i < 2; i++)
            _points[i] = _points[i] with
            {
                TransitionMinutes = _points[i].TransitionMinutes ?? plan.TransitionMinutes,
                Curve =
                    _points[i].Curve
                    ?? (plan.SmoothTransitions ? TransitionCurve.Smooth : TransitionCurve.Linear),
            };
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Ciclo solar";
        BackColor = LightTheme.Background;
        ForeColor = LightTheme.Text;
        Font = new("Segoe UI", 10);
        ClientSize = new(740, 610);
        MinimumSize = new(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = new Icon(Path.Combine(System.AppContext.BaseDirectory, "favicon.ico"));
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(24, 16, 24, 14),
            ColumnCount = 1,
            RowCount = 8,
        };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var h in new[] { 42, 28, 42, 38, 30, -1, 52, 42 })
            root.RowStyles.Add(new(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 100 : h));
        root.Controls.Add(LightTheme.Label("A luz acompanha o sol", 20), 0, 0);
        root.Controls.Add(
            LightTheme.Label(
                $"{scope} · {plan.Location?.Name} · As horas ajustam-se a cada dia do ano.",
                9,
                true
            ),
            0,
            1
        );
        var tabs = LightTheme.Row();
        _morning = LightTheme.Button("Nascer do sol", () => SelectPhase(0));
        _evening = LightTheme.Button("Pôr do sol", () => SelectPhase(1));
        tabs.Controls.Add(_morning);
        tabs.Controls.Add(_evening);
        root.Controls.Add(tabs, 0, 2);
        var timing = LightTheme.Row();
        timing.Controls.Add(LightTheme.Label("Começar"));
        timing.Controls.Add(_offset);
        timing.Controls.Add(LightTheme.Label("min do sol  (− antes / + depois)"));
        timing.Controls.Add(LightTheme.Label("Duração"));
        timing.Controls.Add(_duration);
        timing.Controls.Add(LightTheme.Label("min"));
        root.Controls.Add(timing, 0, 3);
        root.Controls.Add(_summary, 0, 4);
        root.Controls.Add(_curve, 0, 5);
        _description.AutoSize = false;
        _description.Dock = DockStyle.Fill;
        _description.Text =
            "Cria duas transições e substitui os pontos deste horário. Cada ecrã mantém a sua cor mais fria de dia e a mais quente à noite. Podes ajustar as cores e acrescentar pontos depois.";
        root.Controls.Add(_description, 0, 6);
        var buttons = LightTheme.Row();
        buttons.FlowDirection = FlowDirection.RightToLeft;
        var apply = LightTheme.Button(
            scope == "Só hoje" ? "Aplicar só hoje" : "Usar este ciclo no programa",
            () => DialogResult = DialogResult.OK
        );
        var cancel = LightTheme.Button("Cancelar", () => DialogResult = DialogResult.Cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 7);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = cancel;
        _offset.ValueChanged += (_, _) => Change();
        _duration.ValueChanged += (_, _) => Change();
        _curve.Changed += Change;
        LightTheme.Style(this);
        SelectPhase(0);
        AutoScaleDimensions = new(96, 96);
    }

    private void SelectPhase(int index)
    {
        _loading = true;
        _selected = index;
        _offset.Value = _points[index].OffsetMinutes;
        _duration.Value = _points[index].TransitionMinutes!.Value;
        _curve.Curve = _points[index].Curve!;
        _morning.BackColor = index == 0 ? LightTheme.Field : LightTheme.Background;
        _evening.BackColor = index == 1 ? LightTheme.Field : LightTheme.Background;
        _morning.ForeColor = index == 0 ? LightTheme.Accent : LightTheme.Text;
        _evening.ForeColor = index == 1 ? LightTheme.Accent : LightTheme.Text;
        _loading = false;
        RefreshSummary();
    }

    private void Change()
    {
        if (_loading)
            return;
        _points[_selected] = _points[_selected] with
        {
            OffsetMinutes = (int)_offset.Value,
            TransitionMinutes = (int)_duration.Value,
            Curve = _curve.Curve,
        };
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var solar = _plan.Location is null ? null : SolarTimes.Calculate(_date, _plan.Location);
        var time = _selected == 0 ? solar?.Sunrise : solar?.Sunset;
        var point = _points[_selected];
        if (time is null)
        {
            _summary.Text = $"Sem evento solar: início às {point.Time:HH:mm} (hora de recurso).";
            return;
        }
        var start = time.Value.AddMinutes(point.OffsetMinutes);
        var end = start.AddMinutes(point.TransitionMinutes!.Value);
        _summary.Text =
            $"{_date:dd/MM}: sol às {time:HH:mm} · mudança das {start:HH:mm} às {end:HH:mm}";
    }

    internal void VerifyInteraction()
    {
        _offset.Value = -45;
        _duration.Value = 120;
        _curve.VerifyInteraction();
        SelectPhase(1);
        _offset.Value = -90;
        SelectPhase(0);
        if (
            _offset.Value != -45
            || Morning.TransitionMinutes != 120
            || Evening.OffsetMinutes != -90
        )
            throw new InvalidOperationException("Solar phase edits did not stay independent.");
    }
}
