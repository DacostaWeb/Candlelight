using System.Globalization;
using System.Runtime.InteropServices;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class ControlWindow : Form
{
    internal static readonly Color Surface = LightTheme.Background;
    internal static readonly Color TextColor = LightTheme.Text;
    private readonly ProfileStore _store;
    private readonly ProfileController? _controller;
    private readonly FlowLayoutPanel _monitorRow = LightTheme.Row();
    private readonly ScheduleGraph _graph = new()
    {
        Dock = DockStyle.Fill,
        Margin = new(0, 4, 0, 0),
    };
    private readonly CheckBox _enabled = new() { Text = "Filtro ativo", AutoSize = true };
    private readonly CheckBox _scheduled = new() { Text = "Horário ativo", AutoSize = true };
    private readonly CheckBox _shared = new() { Text = "Horários partilhados", AutoSize = true };
    private readonly CheckBox _compare = new() { Text = "Comparar ecrãs", AutoSize = true };
    private readonly CheckBox _red = new() { Text = "Vermelho puro", AutoSize = true };
    private readonly CheckBox _filterCursor = new() { Text = "Filtrar ponteiro", AutoSize = true };
    private readonly ComboBox _points = new DarkComboBox()
    {
        Width = 235,
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Ponto do horário",
    };
    private readonly ComboBox _anchor = new DarkComboBox()
    {
        Width = 160,
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Hora fixa, nascer ou pôr do sol",
    };
    private readonly TextBox _time = new()
    {
        Width = 64,
        AccessibleName = "Hora de início da mudança",
        MaxLength = 5,
    };
    private readonly NumericUpDown _offset = new()
    {
        Minimum = -720,
        Maximum = 720,
        Width = 68,
        Increment = 5,
        AccessibleName = "Minutos antes ou depois do sol",
    };
    private readonly NumericUpDown _transition = new()
    {
        Minimum = 0,
        Maximum = 1440,
        Width = 64,
        Increment = 5,
        AccessibleName = "Duração global das transições",
    };
    private readonly ComboBox _easing = new DarkComboBox()
    {
        Width = 95,
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Curva das transições",
    };
    private readonly NumericUpDown _temperature = new()
    {
        Minimum = 500,
        Maximum = 12000,
        Increment = 50,
        Width = 83,
        AccessibleName = "Temperatura em kelvin",
    };
    private readonly NumericUpDown _brightness = new()
    {
        Minimum = 1,
        Maximum = 100,
        Width = 83,
        AccessibleName = "Brilho em percentagem",
    };
    private readonly ValueSlider _temperatureSlider = new()
    {
        Minimum = 500,
        Maximum = 12000,
        Step = 50,
        Dock = DockStyle.Fill,
        AccessibleName = "Temperatura",
    };
    private readonly ValueSlider _brightnessSlider = new()
    {
        Minimum = 1,
        Maximum = 100,
        Dock = DockStyle.Fill,
        AccessibleName = "Brilho",
    };
    private readonly Label _status = LightTheme.Label("", 9, true);
    private readonly Label _now = LightTheme.Label("", 12);
    private readonly Label _editorTitle = LightTheme.Label("Cor deste ponto", 11);
    private readonly Label _timingHint = LightTheme.Label("", 9, true);
    private readonly Label _solarLabel = LightTheme.Label("", 9, true);
    private readonly Label _dateLabel = LightTheme.Label("Hoje", 10);
    private readonly Label _offsetUnit = LightTheme.Label("min");
    private readonly Button _temperatureTab;
    private readonly Button _brightnessTab;
    private readonly Button _undo;
    private readonly Button _manual;
    private readonly Button _location;
    private readonly Button _remove;
    private readonly Stack<AppSettings> _history = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 30000 };
    private Dictionary<string, DisplayDescriptor> _displays = [];
    private string? _selectedMonitor;
    private string? _selectedPoint;
    private bool _loading;
    private bool _editingManual;
    private bool _brightnessView;
    private DateOnly _date;
    private bool _followingToday = true;
    private string? _monitorSignature;
    private long _messageUntil;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal bool Exiting { get; set; }
    public event Action? SelectionChanged;

    public ControlWindow(
        ProfileStore store,
        ProfileController? controller,
        IEnumerable<DisplayDescriptor> displays
    )
    {
        _store = store;
        _controller = controller;
        _date = Today();
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Candlelight";
        BackColor = Surface;
        ForeColor = TextColor;
        Font = new("Segoe UI", 10);
        ClientSize = new(1060, 760);
        MinimumSize = new(900, 660);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Icon = new Icon(Path.Combine(System.AppContext.BaseDirectory, "favicon.ico"));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(28, 16, 28, 12),
            ColumnCount = 1,
            RowCount = 8,
        };
        foreach (var height in new[] { 48, 54, 36, -1, 42, 174, 34, 24 })
            root.RowStyles.Add(
                new(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height)
            );
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new(0),
        };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.RowCount = 1;
        header.RowStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 240));
        header.ColumnStyles.Add(new(SizeType.Absolute, 110));
        header.Controls.Add(
            new Label
            {
                Text = "Candlelight",
                AutoSize = true,
                Font = new("Bahnschrift SemiLight", 25),
                Margin = new(0),
            },
            0,
            0
        );
        _now.Anchor = AnchorStyles.Right;
        header.Controls.Add(_now, 1, 0);
        _enabled.Anchor = AnchorStyles.Right;
        header.Controls.Add(_enabled, 2, 0);
        root.Controls.Add(header, 0, 0);
        _monitorRow.AutoScroll = true;
        root.Controls.Add(_monitorRow, 0, 1);

        var graphHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new(0),
        };
        graphHeader.ColumnStyles.Add(new(SizeType.Percent, 100));
        graphHeader.RowCount = 1;
        graphHeader.RowStyles.Add(new(SizeType.Percent, 100));
        graphHeader.ColumnStyles.Add(new(SizeType.Absolute, 430));
        var titleRow = LightTheme.Row();
        titleRow.Controls.Add(LightTheme.Label("O teu dia", 15));
        titleRow.Controls.Add(LightTheme.Button("‹", () => ChangeDate(-1)));
        _dateLabel.MinimumSize = new(94, 25);
        _dateLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dateLabel.Margin = new(0, 1, 8, 0);
        titleRow.Controls.Add(_dateLabel);
        titleRow.Controls.Add(LightTheme.Button("›", () => ChangeDate(1)));
        titleRow.Controls.Add(
            LightTheme.Button(
                "Hoje",
                () =>
                {
                    _followingToday = true;
                    _date = Today();
                    Reload();
                }
            )
        );
        graphHeader.Controls.Add(titleRow, 0, 0);
        var tabs = LightTheme.Row();
        tabs.FlowDirection = FlowDirection.RightToLeft;
        _temperatureTab = LightTheme.Button(
            "Temperatura",
            () =>
            {
                _brightnessView = false;
                Reload();
            }
        );
        _brightnessTab = LightTheme.Button(
            "Brilho",
            () =>
            {
                _brightnessView = true;
                Reload();
            }
        );
        tabs.Controls.Add(_compare);
        _compare.Margin = new(8, 7, 0, 0);
        tabs.Controls.Add(_brightnessTab);
        tabs.Controls.Add(_temperatureTab);
        graphHeader.Controls.Add(tabs, 1, 0);
        root.Controls.Add(graphHeader, 0, 2);
        root.Controls.Add(_graph, 0, 3);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new(0, 7, 0, 0),
        };
        toolbar.ColumnStyles.Add(new(SizeType.Percent, 100));
        toolbar.RowCount = 1;
        toolbar.RowStyles.Add(new(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new(SizeType.Absolute, 330));
        var tools = LightTheme.Row();
        tools.Controls.Add(LightTheme.Button("+ Ponto", AddPoint));
        tools.Controls.Add(LightTheme.Button("Copiar cores…", CopyColors));
        _undo = LightTheme.Button("Desfazer", Undo);
        _undo.AccessibleDescription = "Desfaz a última alteração. Ctrl+Z.";
        tools.Controls.Add(_undo);
        toolbar.Controls.Add(tools, 0, 0);
        var transitions = LightTheme.Row();
        transitions.Controls.Add(LightTheme.Label("Transição global"));
        transitions.Controls.Add(_transition);
        transitions.Controls.Add(LightTheme.Label("min"));
        _easing.Items.AddRange(["Suaves", "Lineares"]);
        transitions.Controls.Add(_easing);
        toolbar.Controls.Add(transitions, 1, 0);
        root.Controls.Add(toolbar, 0, 4);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new(0),
            Padding = new(0, 10, 0, 0),
        };
        editor.ColumnStyles.Add(new(SizeType.Absolute, 300));
        editor.RowCount = 1;
        editor.RowStyles.Add(new(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new(SizeType.Percent, 100));
        var timing = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Margin = new(0, 0, 20, 0),
        };
        foreach (var h in new[] { 28, 34, 34, 28, 32 })
            timing.RowStyles.Add(new(SizeType.Absolute, h));
        var sharing = LightTheme.Row();
        sharing.Controls.Add(_shared);
        timing.Controls.Add(sharing, 0, 0);
        var pointRow = LightTheme.Row();
        pointRow.Controls.Add(_points);
        _remove = LightTheme.Button("−", RemovePoint);
        _remove.AccessibleName = "Remover ponto selecionado";
        pointRow.Controls.Add(_remove);
        timing.Controls.Add(pointRow, 0, 1);
        var timeRow = LightTheme.Row();
        _anchor.Items.AddRange(["Hora fixa", "Nascer do sol", "Pôr do sol"]);
        timeRow.Controls.Add(_anchor);
        timeRow.Controls.Add(_time);
        timeRow.Controls.Add(_offset);
        timeRow.Controls.Add(_offsetUnit);
        timing.Controls.Add(timeRow, 0, 2);
        timing.Controls.Add(_scheduled, 0, 3);
        _timingHint.MaximumSize = new(280, 0);
        timing.Controls.Add(_timingHint, 0, 4);
        editor.Controls.Add(timing, 0, 0);

        var color = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 4,
            Margin = new(0),
        };
        color.ColumnStyles.Add(new(SizeType.Absolute, 98));
        color.ColumnStyles.Add(new(SizeType.Percent, 100));
        color.ColumnStyles.Add(new(SizeType.Absolute, 88));
        color.ColumnStyles.Add(new(SizeType.Absolute, 25));
        foreach (var h in new[] { 32, 35, 35, 42 })
            color.RowStyles.Add(new(SizeType.Absolute, h));
        var colorTitle = LightTheme.Row();
        colorTitle.Controls.Add(_editorTitle);
        _manual = LightTheme.Button("Ajustar agora", StartManualEdit);
        colorTitle.Controls.Add(_manual);
        color.Controls.Add(colorTitle, 0, 0);
        color.SetColumnSpan(colorTitle, 4);
        color.Controls.Add(LightTheme.Label("Temperatura"), 0, 1);
        color.Controls.Add(_temperatureSlider, 1, 1);
        color.Controls.Add(_temperature, 2, 1);
        color.Controls.Add(LightTheme.Label("K"), 3, 1);
        color.Controls.Add(LightTheme.Label("Brilho"), 0, 2);
        color.Controls.Add(_brightnessSlider, 1, 2);
        color.Controls.Add(_brightness, 2, 2);
        color.Controls.Add(LightTheme.Label("%"), 3, 2);
        var presets = LightTheme.Row();
        _red.Margin = new(0, 7, 15, 0);
        presets.Controls.Add(_red);
        presets.Controls.Add(LightTheme.Button("Presets…", ShowPresets));
        presets.Controls.Add(LightTheme.Button("Guardar preset", SavePreset));
        color.Controls.Add(presets, 0, 3);
        color.SetColumnSpan(presets, 4);
        editor.Controls.Add(color, 1, 0);
        root.Controls.Add(editor, 0, 5);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new(0),
        };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.RowCount = 1;
        footer.RowStyles.Add(new(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new(SizeType.Absolute, 150));
        var solarRow = LightTheme.Row();
        _location = LightTheme.Button("Escolher cidade", () => ChooseLocation());
        solarRow.Controls.Add(_location);
        solarRow.Controls.Add(_solarLabel);
        footer.Controls.Add(solarRow, 0, 0);
        _filterCursor.Anchor = AnchorStyles.Right;
        footer.Controls.Add(_filterCursor, 1, 0);
        root.Controls.Add(footer, 0, 6);
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        root.Controls.Add(_status, 0, 7);
        LightTheme.Style(this);
        WireEvents();
        UpdateDisplays(displays);
        Reload();
        AutoScaleDimensions = new(96, 96);
        _clock.Tick += (_, _) =>
        {
            if (_followingToday && _date != Today())
            {
                _date = Today();
                Reload();
                return;
            }
            RefreshGraph();
            UpdateSolar(_store.Read());
        };
        _clock.Start();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height - 20 || Width > area.Width - 20)
            {
                MinimumSize = new(
                    Math.Min(MinimumSize.Width, area.Width - 20),
                    Math.Min(MinimumSize.Height, area.Height - 20)
                );
                Size = new(Math.Min(Width, area.Width - 20), Math.Min(Height, area.Height - 20));
                Location = new(
                    area.Left + (area.Width - Width) / 2,
                    area.Top + (area.Height - Height) / 2
                );
            }
        };
        FormClosing += (_, e) =>
        {
            if (!Exiting)
            {
                e.Cancel = true;
                Hide();
            }
        };
        FormClosed += (_, _) => _clock.Dispose();
    }

    private void WireEvents()
    {
        _enabled.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Commit(s => Current(s).Enabled = _enabled.Checked);
        };
        _scheduled.CheckedChanged += (_, _) =>
        {
            if (!_loading)
            {
                _editingManual = false;
                Commit(s => Current(s).Scheduled = _scheduled.Checked);
            }
        };
        _shared.CheckedChanged += (_, _) =>
        {
            if (!_loading && _selectedMonitor is not null)
                Commit(s => ScheduleEditing.SetShared(s, _selectedMonitor, _shared.Checked, _date));
        };
        _compare.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                RefreshGraph();
        };
        _filterCursor.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Commit(s => s.FilterCursor = _filterCursor.Checked);
        };
        _transition.ValueChanged += (_, _) =>
        {
            if (!_loading)
                Commit(s => s.Timing.TransitionMinutes = (int)_transition.Value);
        };
        _easing.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
                Commit(s => s.Timing.SmoothTransitions = _easing.SelectedIndex == 0);
        };
        _points.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _points.SelectedItem is PointChoice point)
            {
                _selectedPoint = point.Id;
                _editingManual = false;
                Reload();
            }
        };
        _anchor.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
                return;
            var anchor = (TimeAnchor)_anchor.SelectedIndex;
            if (
                anchor != TimeAnchor.Clock
                && _store.Read().Timing.Location is null
                && !ChooseLocation()
            )
            {
                Reload();
                return;
            }
            EditTiming(p => p with { Anchor = anchor, OffsetMinutes = 0 });
        };
        _time.Leave += (_, _) => SaveClockTime();
        _time.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SaveClockTime();
                e.SuppressKeyPress = true;
            }
        };
        _offset.ValueChanged += (_, _) =>
        {
            if (!_loading)
                EditTiming(p => p with { OffsetMinutes = (int)_offset.Value });
        };
        _temperature.ValueChanged += (_, _) => ColorChanged();
        _brightness.ValueChanged += (_, _) => ColorChanged();
        _red.CheckedChanged += (_, _) => ColorChanged();
        _temperatureSlider.ValueChanged += (_, _) =>
        {
            if (!_loading)
                _temperature.Value = _temperatureSlider.Value;
        };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            if (!_loading)
                _brightness.Value = _brightnessSlider.Value;
        };
        _temperatureSlider.InteractionCompleted += (_, _) => ColorChanged();
        _brightnessSlider.InteractionCompleted += (_, _) => ColorChanged();
        _graph.PointSelected += id =>
        {
            _selectedPoint = id;
            _editingManual = false;
            Reload();
        };
        _graph.PointEdited += (point, color) =>
        {
            if (_selectedMonitor is not null)
                Commit(s => ScheduleEditing.SetPoint(s, _selectedMonitor, point, color));
        };
        _graph.PointAdded += (time, value) => AddPointAt(time, value);
        _graph.PointRemoved += id =>
        {
            _selectedPoint = id;
            RemovePoint();
        };
        _graph.EditRequested += () => _time.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
                e.SuppressKeyPress = true;
            }
        };
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, _store.Read().Timing.TimeZone).DateTime
        );

    private MonitorProfile Current(AppSettings settings) =>
        settings.Monitors.Single(m => m.Id == _selectedMonitor);

    private TimingPoint? CurrentPoint(AppSettings settings) =>
        ScheduleTimeline
            .Points(settings.Timing, Current(settings))
            .FirstOrDefault(p => p.Id == _selectedPoint);

    private ColorProfile ReadColor() =>
        new(
            _red.Checked ? ColorMode.PureRed : ColorMode.Temperature,
            (double)_temperature.Value,
            (double)_brightness.Value / 100
        );

    private void ChangeDate(int amount)
    {
        _date = _date.AddDays(amount);
        _followingToday = _date == Today();
        Reload();
    }

    public void UpdateDisplays(IEnumerable<DisplayDescriptor> displays)
    {
        _displays = displays.ToDictionary(d => d.Id);
        var settings = _store.Read();
        _selectedMonitor ??= settings.SelectedMonitor;
        if (!settings.Monitors.Any(m => m.Id == _selectedMonitor))
            _selectedMonitor =
                _displays.Keys.FirstOrDefault() ?? settings.Monitors.FirstOrDefault()?.Id;
        RebuildMonitors(settings);
    }

    private string MonitorName(MonitorProfile monitor) =>
        monitor.Name == "Generic PnP Monitor" ? "Ecrã interno"
        : monitor.Name == "13A1F" ? "OLED · 13A1F"
        : monitor.Name;

    private void RebuildMonitors(AppSettings settings)
    {
        var monitors = settings
            .Monitors.Where(m =>
                _displays.ContainsKey(m.Id) || m.Id.StartsWith("edid:") || m.Id == _selectedMonitor
            )
            .OrderByDescending(m => _displays.ContainsKey(m.Id))
            .ToArray();
        var signature =
            string.Join(
                "|",
                monitors.Select(m =>
                    $"{m.Id}:{m.UseSharedSchedule}:{m.Scheduled}:{m.Enabled}:{_displays.ContainsKey(m.Id)}"
                )
            ) + _selectedMonitor;
        if (signature == _monitorSignature)
            return;
        _monitorSignature = signature;
        foreach (Control control in _monitorRow.Controls.Cast<Control>().ToArray())
            control.Dispose();
        _monitorRow.Controls.Clear();
        foreach (var monitor in monitors)
        {
            var detail =
                !_displays.ContainsKey(monitor.Id) ? "Desligado · perfil guardado"
                : !monitor.Enabled ? "Filtro em pausa"
                : !monitor.Scheduled ? "Ajuste manual"
                : monitor.UseSharedSchedule ? "Horas comuns · cor própria"
                : "Horário independente";
            var tab = new MonitorTab(
                MonitorName(monitor),
                detail,
                monitor.Id == _selectedMonitor,
                () =>
                {
                    _selectedMonitor = monitor.Id;
                    _editingManual = false;
                    _store.Update(s => s.SelectedMonitor = monitor.Id);
                    Reload();
                    SelectionChanged?.Invoke();
                }
            );
            if (IsHandleCreated)
                tab.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            _monitorRow.Controls.Add(tab);
        }
    }

    public void Reload()
    {
        if (_selectedMonitor is null)
            return;
        var settings = _store.Read();
        var monitor = Current(settings);
        var nodes = ScheduleTimeline.ResolveDay(settings.Timing, monitor, _date);
        if (!nodes.Any(n => n.Point.Id == _selectedPoint))
            _selectedPoint =
                nodes.LastOrDefault(n => n.Time <= DateTimeOffset.Now)?.Point.Id
                ?? nodes.FirstOrDefault()?.Point.Id;
        var point = nodes.FirstOrDefault(n => n.Point.Id == _selectedPoint);
        var color =
            _editingManual || point is null
                ? monitor.Manual
                : ScheduleTimeline.Color(monitor, point.Point);
        _loading = true;
        try
        {
            _enabled.Checked = monitor.Enabled;
            _scheduled.Checked = monitor.Scheduled;
            _shared.Checked = monitor.UseSharedSchedule;
            _filterCursor.Checked = settings.FilterCursor;
            _transition.Value = settings.Timing.TransitionMinutes;
            _easing.SelectedIndex = settings.Timing.SmoothTransitions ? 0 : 1;
            _points.Items.Clear();
            _points.Items.AddRange(
                nodes
                    .Select(n => new PointChoice(
                        n.Point.Id,
                        $"{n.Time:HH:mm}  {AnchorName(n.Point.Anchor)}"
                    ))
                    .ToArray()
            );
            _points.SelectedIndex = Array.FindIndex(nodes, n => n.Point.Id == _selectedPoint);
            _anchor.SelectedIndex = (int)(point?.Point.Anchor ?? TimeAnchor.Clock);
            _time.Text = (point?.Point.Time ?? new TimeOnly(22, 0)).ToString("HH:mm");
            _offset.Value = point?.Point.OffsetMinutes ?? 0;
            _time.Visible = _anchor.SelectedIndex == 0;
            _offset.Visible = _offsetUnit.Visible = !_time.Visible;
            _anchor.Enabled =
                _time.Enabled =
                _offset.Enabled =
                    !_editingManual && point is not null;
            _remove.Enabled = nodes.Length > 1 && !_editingManual;
            _temperature.Value = (decimal)color.Temperature;
            _brightness.Value = (decimal)Math.Round(color.Brightness * 100);
            _temperatureSlider.Value = (int)color.Temperature;
            _brightnessSlider.Value = (int)Math.Round(color.Brightness * 100);
            _red.Checked = color.Mode == ColorMode.PureRed;
            _temperature.Enabled = _temperatureSlider.Enabled = !_red.Checked;
            _editorTitle.Text = _editingManual
                ? "Ajuste manual deste ecrã"
                : $"Cor a partir das {point?.Time:HH:mm}";
            _manual.Visible = !_editingManual;
            _timingHint.Text =
                _editingManual ? "Escolhe um ponto para voltar ao horário."
                : point?.UsedFallback == true ? "Sem evento solar: usa a hora fixa deste ponto."
                : point?.Point.Anchor != TimeAnchor.Clock
                    ? "− antes do sol; + depois. Atualiza todos os dias."
                : "Os pontos marcam o início da mudança.";
            _temperatureTab.BackColor = !_brightnessView ? LightTheme.Field : LightTheme.Background;
            _brightnessTab.BackColor = _brightnessView ? LightTheme.Field : LightTheme.Background;
            _temperatureTab.ForeColor = !_brightnessView ? LightTheme.Accent : TextColor;
            _brightnessTab.ForeColor = _brightnessView ? LightTheme.Copper : TextColor;
            _dateLabel.Text = _date.ToString("dd MMM", CultureInfo.GetCultureInfo("pt-PT"));
            _undo.Enabled = _history.Count > 0;
            UpdateSolar(settings);
            RebuildMonitors(settings);
            RefreshGraph();
        }
        finally
        {
            _loading = false;
        }
    }

    private static string AnchorName(TimeAnchor anchor) =>
        anchor switch
        {
            TimeAnchor.Sunrise => "Nascer do sol",
            TimeAnchor.Sunset => "Pôr do sol",
            _ => "Hora fixa",
        };

    private void RefreshGraph()
    {
        if (_selectedMonitor is null)
            return;
        var s = _store.Read();
        _graph.SetData(
            s.Timing,
            Current(s),
            s.Monitors.Where(m => _displays.ContainsKey(m.Id)),
            _date,
            _selectedPoint,
            _brightnessView,
            _compare.Checked
        );
    }

    private void UpdateSolar(AppSettings settings)
    {
        var location = settings.Timing.Location;
        _location.Text = location?.Name ?? "Escolher cidade";
        if (location is null)
        {
            _solarLabel.Text = "Liga pontos ao nascer e pôr do sol.";
            return;
        }
        var day = SolarTimes.Calculate(_date, location);
        _solarLabel.Text = day.Sunrise is null
            ? day.PolarDay
                ? "Sol acima do horizonte todo o dia"
                : "Sol abaixo do horizonte todo o dia"
            : $"Nascer {day.Sunrise:HH:mm}   Pôr {day.Sunset:HH:mm}   ·   Calculado para cada dia";
    }

    private bool Commit(Action<AppSettings> edit, bool remember = true)
    {
        try
        {
            var before = _store.Read();
            _store.Update(edit);
            if (remember)
            {
                _history.Push(before);
                if (_history.Count > 30)
                {
                    var keep = _history.Take(30).Reverse().ToArray();
                    _history.Clear();
                    foreach (var item in keep)
                        _history.Push(item);
                }
            }
            Reload();
            _ = ApplyAsync();
            return true;
        }
        catch (Exception error)
        {
            Reload();
            Message(error.Message);
            return false;
        }
    }

    private async Task ApplyAsync()
    {
        try
        {
            if (_controller is not null)
                await _controller.ApplyNowAsync();
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                Message("Não foi possível aplicar: " + error.Message);
        }
    }

    private void Message(string text)
    {
        _status.Text = text;
        _messageUntil = Environment.TickCount64 + 6000;
    }

    private void ColorChanged()
    {
        if (
            _loading
            || _selectedMonitor is null
            || _temperatureSlider.Capture
            || _brightnessSlider.Capture
        )
            return;
        var color = ReadColor();
        if (_editingManual)
            Commit(s =>
            {
                var m = Current(s);
                m.Manual = color;
                m.Scheduled = false;
                m.Enabled = true;
            });
        else if (_selectedPoint is not null)
            Commit(s => Current(s).PointColors[_selectedPoint] = color);
    }

    private void EditTiming(Func<TimingPoint, TimingPoint> edit)
    {
        if (_loading || _selectedMonitor is null || _selectedPoint is null)
            return;
        Commit(s =>
        {
            var p = CurrentPoint(s)!;
            ScheduleEditing.SetPoint(
                s,
                _selectedMonitor,
                edit(p),
                ScheduleTimeline.Color(Current(s), p)
            );
        });
    }

    private void SaveClockTime()
    {
        if (_loading || _editingManual || !_time.Visible)
            return;
        if (
            !TimeOnly.TryParseExact(
                _time.Text,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time
            )
        )
        {
            Message("Escreve a hora como 22:30.");
            return;
        }
        var s = _store.Read();
        if (CurrentPoint(s)?.Time != time)
            EditTiming(p => p with { Time = time });
    }

    private void AddPoint()
    {
        var s = _store.Read();
        var taken = ScheduleTimeline
            .Points(s.Timing, Current(s))
            .Where(p => p.Anchor == TimeAnchor.Clock)
            .Select(p => p.Time)
            .ToHashSet();
        var hour = TimeOnly.FromDateTime(DateTime.Now).Hour;
        var time = Enumerable
            .Range(0, 24)
            .Select(i => new TimeOnly((hour + i) % 24, 0))
            .FirstOrDefault(t => !taken.Contains(t));
        AddPointAt(time, null);
    }

    private void AddPointAt(TimeOnly time, double? value)
    {
        if (_selectedMonitor is null)
            return;
        string? id = null;
        if (
            Commit(s =>
            {
                var monitor = Current(s);
                var color = ScheduleTimeline.Evaluate(
                    s.Timing,
                    monitor with
                    {
                        Scheduled = true,
                    },
                    SolarTimes.AtLocalTime(_date, time, s.Timing.TimeZone)
                );
                if (value is { } amount)
                    color = _brightnessView
                        ? color with
                        {
                            Brightness = Math.Round(amount) / 100,
                        }
                        : color with
                        {
                            Mode = ColorMode.Temperature,
                            Temperature = Math.Clamp(Math.Round(amount / 50) * 50, 500, 12000),
                        };
                id = ScheduleEditing.AddPoint(s, _selectedMonitor, time, color, _date);
            })
        )
        {
            _selectedPoint = id;
            _editingManual = false;
            Reload();
        }
    }

    private void RemovePoint()
    {
        if (_selectedMonitor is not null && _selectedPoint is not null)
            Commit(s => ScheduleEditing.RemovePoint(s, _selectedMonitor, _selectedPoint));
    }

    private void Undo()
    {
        if (_history.Count == 0)
            return;
        var saved = _history.Pop();
        Commit(
            s =>
            {
                s.Monitors = saved.Monitors;
                s.Timing = saved.Timing;
                s.FilterCursor = saved.FilterCursor;
                s.Presets = saved.Presets;
            },
            false
        );
        Message("Alteração desfeita.");
    }

    private void CopyColors()
    {
        var settings = _store.Read();
        var menu = new ContextMenuStrip { BackColor = LightTheme.Field, ForeColor = TextColor };
        foreach (
            var monitor in settings.Monitors.Where(m =>
                m.Id != _selectedMonitor
                && (_displays.ContainsKey(m.Id) || m.Id.StartsWith("edid:"))
            )
        )
            menu.Items.Add(
                "Copiar de " + MonitorName(monitor),
                null,
                (_, _) =>
                {
                    if (_selectedMonitor is not null)
                        Commit(s =>
                            ScheduleEditing.CopyColors(s, _selectedMonitor, monitor.Id, _date)
                        );
                }
            );
        if (menu.Items.Count == 0)
        {
            Message("Liga outro monitor para copiar as suas cores.");
            menu.Dispose();
            return;
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(Cursor.Position);
    }

    private void ShowPresets()
    {
        var menu = new ContextMenuStrip { BackColor = LightTheme.Field, ForeColor = TextColor };
        foreach (var preset in _store.Read().Presets)
            menu.Items.Add(
                preset.Name,
                null,
                (_, _) =>
                {
                    if (_editingManual)
                        ApplyPreset(preset.Profile);
                    else if (_selectedPoint is not null)
                        Commit(s => Current(s).PointColors[_selectedPoint] = preset.Profile);
                }
            );
        var delete = new ToolStripMenuItem("Apagar preset");
        foreach (var preset in _store.Read().Presets)
            delete.DropDownItems.Add(
                preset.Name,
                null,
                (_, _) => Commit(s => s.Presets.RemoveAll(p => p.Name == preset.Name))
            );
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(delete);
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(Cursor.Position);
    }

    private void SavePreset()
    {
        using var dialog = new Form
        {
            Text = "Guardar preset",
            BackColor = Surface,
            ForeColor = TextColor,
            Font = Font,
            ClientSize = new(360, 126),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var name = new TextBox
        {
            Left = 18,
            Top = 22,
            Width = 320,
            AccessibleName = "Nome do preset",
        };
        var save = LightTheme.Button(
            "Guardar",
            () =>
            {
                if (!string.IsNullOrWhiteSpace(name.Text))
                    dialog.DialogResult = DialogResult.OK;
            }
        );
        save.Location = new(230, 72);
        dialog.Controls.AddRange([name, save]);
        dialog.AcceptButton = save;
        LightTheme.Style(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Commit(s =>
            {
                s.Presets.RemoveAll(p =>
                    p.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                );
                s.Presets.Add(new(name.Text.Trim(), ReadColor()));
            });
    }

    private bool ChooseLocation()
    {
        using var dialog = new LocationDialog(_store.Read().Timing.Location);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedLocation is null)
            return false;
        return Commit(s => s.Timing.Location = dialog.SelectedLocation);
    }

    public void ApplyPreset(ColorProfile profile)
    {
        if (_selectedMonitor is null)
            return;
        _editingManual = true;
        Commit(s =>
        {
            var m = Current(s);
            m.Manual = profile;
            m.Scheduled = false;
            m.Enabled = true;
        });
    }

    private void StartManualEdit()
    {
        _editingManual = true;
        Commit(s =>
        {
            var monitor = Current(s);
            monitor.Manual = ScheduleTimeline.Evaluate(s.Timing, monitor, DateTimeOffset.Now);
            monitor.Scheduled = false;
        });
    }

    public void ResumeSchedule()
    {
        if (_selectedMonitor is null)
            return;
        _editingManual = false;
        Commit(s =>
        {
            var monitor = Current(s);
            monitor.Scheduled = true;
            monitor.Enabled = true;
        });
    }

    public void ShowStatus(EngineSnapshot snapshot)
    {
        if (IsDisposed)
            return;
        UpdateDisplays(snapshot.Monitors.Select(m => m.Display));
        var state = snapshot.Monitors.FirstOrDefault(m => m.Display.Id == _selectedMonitor);
        var color =
            state?.Profile.Mode == ColorMode.PureRed
                ? "Vermelho puro"
                : $"{state?.Profile.Temperature:0} K";
        _now.Text =
            state is null ? "Ecrã desligado"
            : !state.Enabled ? "Filtro em pausa"
            : $"{color}  /  {state.Profile.Brightness:P0}";
        if (Environment.TickCount64 < _messageUntil)
            return;
        var monitor = _store.Read().Monitors.FirstOrDefault(m => m.Id == _selectedMonitor);
        _status.Text =
            _controller?.Error
            ?? snapshot.CursorError
            ?? state?.Error
            ?? (
                state is null ? "Perfil guardado. Será aplicado quando ligares este monitor."
                : state.Black ? "A retomar o filtro…"
                : !state.Enabled ? "Filtro em pausa neste ecrã."
                : monitor?.Scheduled != true
                    ? "Ajuste manual ativo. Ativa o horário para retomar as mudanças automáticas."
                : "Arrasta os pontos para ajustar. Duplo clique adiciona um ponto. Ctrl+Z desfaz."
            );
    }

    internal void VerifyPreviewControls()
    {
        var selected = _selectedMonitor!;
        var other = _store.Read().Monitors.First(m => m.Id != selected);
        var otherColors = new Dictionary<string, ColorProfile>(other.PointColors);
        _selectedPoint = _store.Read().Timing.Points[0].Id;
        _editingManual = false;
        Reload();
        var originalTime = CurrentPoint(_store.Read())!.Time;
        _graph.ExerciseDrag(
            _selectedPoint,
            8 * 60 + 45,
            3000,
            () =>
            {
                if (CurrentPoint(_store.Read())!.Time != originalTime)
                    throw new InvalidOperationException("Dragging persisted before release.");
            }
        );
        if (CurrentPoint(_store.Read())!.Time != new TimeOnly(8, 45))
            throw new InvalidOperationException("Graph drag did not save the shared time.");
        _graph.ExerciseDrag(_selectedPoint, 8 * 60, 4000, () => { }, true);
        if (CurrentPoint(_store.Read())!.Time != new TimeOnly(8, 45))
            throw new InvalidOperationException("Escape did not cancel graph drag.");
        _graph.ExerciseKey(Keys.Right);
        if (CurrentPoint(_store.Read())!.Time != new TimeOnly(8, 50))
            throw new InvalidOperationException("Graph keyboard editing failed.");
        _temperature.Value = 4000;
        if (Current(_store.Read()).PointColors[_selectedPoint].Temperature != 4000)
            throw new InvalidOperationException("Point numeric binding failed.");
        _temperature.Value = 2700;
        _transition.Value = 30;
        if (_store.Read().Timing.TransitionMinutes != 30)
            throw new InvalidOperationException("Global transition binding failed.");
        _time.Text = "08:30";
        SaveClockTime();
        var changed = _store.Read();
        if (
            ScheduleTimeline
                .Points(changed.Timing, changed.Monitors.Single(m => m.Id == other.Id))[0]
                .Time != new TimeOnly(8, 30)
        )
            throw new InvalidOperationException("Shared hour binding failed.");
        if (
            otherColors.Any(p =>
                changed.Monitors.Single(m => m.Id == other.Id).PointColors[p.Key] != p.Value
            )
        )
            throw new InvalidOperationException("Editing changed other screen colors.");
        Undo();
        _shared.Checked = false;
        if (Current(_store.Read()).UseSharedSchedule)
            throw new InvalidOperationException("Detach binding failed.");
        _shared.Checked = true;
        _anchor.SelectedIndex = 1;
        if (CurrentPoint(_store.Read())?.Anchor != TimeAnchor.Sunrise)
            throw new InvalidOperationException("Solar point binding failed.");
        _offset.Value = 20;
        _brightnessView = true;
        Reload();
        _brightness.Value = 35;
        if (Current(_store.Read()).PointColors[_selectedPoint].Brightness != .35)
            throw new InvalidOperationException("Point brightness binding failed.");
        ApplyPreset(new(ColorMode.PureRed, 500, .15));
        if (!_red.Checked || _temperature.Enabled || Current(_store.Read()).Scheduled)
            throw new InvalidOperationException("Manual red override failed.");
        _scheduled.Checked = true;
        _filterCursor.Checked = false;
        if (_store.Read().FilterCursor)
            throw new InvalidOperationException("Cursor binding failed.");
        _filterCursor.Checked = true;
        _brightnessView = false;
        _editingManual = false;
        Reload();
    }

    internal void PreparePreview(bool brightness, bool oled = false)
    {
        _selectedMonitor = oled ? "preview-oled" : "preview-internal";
        _selectedPoint = _store.Read().Timing.Points.Last().Id;
        _brightnessView = brightness;
        _editingManual = false;
        _history.Clear();
        _messageUntil = 0;
        Reload();
        _graph.StaticPreview();
        ActiveControl = _brightnessView ? _brightnessTab : _temperatureTab;
        var settings = _store.Read();
        var profile = ScheduleTimeline.Evaluate(
            settings.Timing,
            Current(settings),
            DateTimeOffset.Now
        );
        _now.Text =
            $"{(profile.Mode == ColorMode.PureRed ? "Vermelho puro" : $"{profile.Temperature:0} K")}  /  {profile.Brightness:P0}";
        _status.Text =
            "Arrasta os pontos para ajustar. Duplo clique adiciona um ponto. Ctrl+Z desfaz.";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var enabled = 1;
        DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }

    private sealed record PointChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int size
    );
}
