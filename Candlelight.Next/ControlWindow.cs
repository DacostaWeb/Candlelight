using System.Globalization;
using System.Runtime.InteropServices;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class ControlWindow : Form
{
    internal static readonly Color Surface = Color.FromArgb(23, 27, 29);
    internal static readonly Color TextColor = Color.FromArgb(237, 222, 203);
    private static readonly Color Field = Color.FromArgb(38, 46, 47);
    private static readonly Color Muted = Color.FromArgb(171, 183, 177);
    private readonly ProfileStore _store;
    private readonly ProfileController? _controller;
    private readonly ComboBox _monitors = new DarkComboBox(),
        _presets = new DarkComboBox();
    private readonly CheckBox _enabled = new() { Text = "Filtro ativo", AutoSize = true };
    private readonly CheckBox _red = new() { Text = "Vermelho puro", AutoSize = true };
    private readonly CheckBox _filterCursor = new()
    {
        Text = "Filtrar ponteiro",
        AutoSize = true,
        Margin = new(20, 3, 0, 3),
    };
    private readonly CheckBox _scheduled = new()
    {
        Text = "Usar horários neste monitor",
        AutoSize = true,
    };
    private readonly ValueSlider _temperatureSlider = new()
    {
        Minimum = 500,
        Maximum = 12000,
        Step = 50,
        AccessibleName = "Temperatura",
    };
    private readonly ValueSlider _brightnessSlider = new()
    {
        Minimum = 1,
        Maximum = 100,
        AccessibleName = "Brilho",
    };
    private readonly NumericUpDown _temperature = new()
    {
        Minimum = 500,
        Maximum = 12000,
        Increment = 50,
        Width = 95,
        AccessibleName = "Temperatura em kelvin",
    };
    private readonly NumericUpDown _brightness = new()
    {
        Minimum = 1,
        Maximum = 100,
        Width = 95,
        AccessibleName = "Brilho em percentagem",
    };
    private readonly ListBox _schedule = new()
    {
        Height = 112,
        IntegralHeight = false,
        Dock = DockStyle.Fill,
    };
    private readonly TextBox _time = new()
    {
        Text = "22:00",
        Width = 64,
        AccessibleName = "Hora",
    };
    private readonly NumericUpDown _transition = new()
    {
        Minimum = 0,
        Maximum = 1440,
        Width = 64,
        AccessibleName = "Minutos de transição",
    };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Muted };
    private bool _loading;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden
    )]
    internal bool Exiting { get; set; }
    public event Action? SelectionChanged;
    private string? SelectedId => (_monitors.SelectedItem as MonitorChoice)?.Id;

    public ControlWindow(
        ProfileStore store,
        ProfileController? controller,
        IEnumerable<DisplayDescriptor> displays
    )
    {
        _store = store;
        _controller = controller;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Candlelight";
        BackColor = Surface;
        ForeColor = TextColor;
        Font = new("Segoe UI", 10);
        ClientSize = new(670, 630);
        MinimumSize = new(620, 630);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = new Icon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "favicon.ico"));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(24, 16, 24, 16),
            ColumnCount = 1,
            RowCount = 10,
        };
        root.RowStyles.Add(new(SizeType.Absolute, 40));
        root.RowStyles.Add(new(SizeType.Absolute, 36));
        root.RowStyles.Add(new(SizeType.Absolute, 90));
        root.RowStyles.Add(new(SizeType.Absolute, 78));
        root.RowStyles.Add(new(SizeType.Absolute, 36));
        root.RowStyles.Add(new(SizeType.Absolute, 36));
        root.RowStyles.Add(new(SizeType.Absolute, 32));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 42));
        root.RowStyles.Add(new(SizeType.Absolute, 40));
        Controls.Add(root);
        root.Controls.Add(
            new Label
            {
                Text = "Cor e brilho",
                Font = new("Segoe UI", 22),
                AutoSize = true,
            },
            0,
            0
        );

        var monitorRow = Flow();
        _monitors.Width = 355;
        _monitors.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitors.AccessibleName = "Monitor";
        monitorRow.Controls.Add(_monitors);
        _enabled.Margin = new(20, 6, 0, 0);
        monitorRow.Controls.Add(_enabled);
        root.Controls.Add(monitorRow, 0, 1);

        var temperaturePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };
        temperaturePanel.ColumnStyles.Add(new(SizeType.Percent, 100));
        temperaturePanel.ColumnStyles.Add(new(SizeType.Absolute, 120));
        temperaturePanel.RowStyles.Add(new(SizeType.Absolute, 26));
        temperaturePanel.RowStyles.Add(new(SizeType.Absolute, 34));
        temperaturePanel.RowStyles.Add(new(SizeType.Absolute, 30));
        temperaturePanel.Controls.Add(Label("Temperatura"), 0, 0);
        _temperatureSlider.Dock = DockStyle.Fill;
        temperaturePanel.Controls.Add(_temperatureSlider, 0, 1);
        temperaturePanel.Controls.Add(WithUnit(_temperature, "K"), 1, 1);
        var colorOptions = Flow();
        colorOptions.Controls.Add(_red);
        colorOptions.Controls.Add(_filterCursor);
        temperaturePanel.Controls.Add(colorOptions, 0, 2);
        temperaturePanel.SetColumnSpan(colorOptions, 2);
        root.Controls.Add(temperaturePanel, 0, 2);

        var brightnessPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };
        brightnessPanel.ColumnStyles.Add(new(SizeType.Percent, 100));
        brightnessPanel.ColumnStyles.Add(new(SizeType.Absolute, 120));
        brightnessPanel.RowStyles.Add(new(SizeType.Absolute, 24));
        brightnessPanel.RowStyles.Add(new(SizeType.Absolute, 32));
        brightnessPanel.RowStyles.Add(new(SizeType.Absolute, 22));
        brightnessPanel.Controls.Add(Label("Brilho"), 0, 0);
        _brightnessSlider.Dock = DockStyle.Fill;
        brightnessPanel.Controls.Add(_brightnessSlider, 0, 1);
        brightnessPanel.Controls.Add(WithUnit(_brightness, "%"), 1, 1);
        brightnessPanel.Controls.Add(
            new Label
            {
                Text = "100% mantém o brilho original deste monitor.",
                AutoSize = true,
                ForeColor = Muted,
                Font = new("Segoe UI", 9),
            },
            0,
            2
        );
        root.Controls.Add(brightnessPanel, 0, 3);

        var quick = Flow();
        foreach (var preset in new AppSettings().Presets)
            quick.Controls.Add(Button(preset.Name, () => ApplyPreset(preset.Profile)));
        root.Controls.Add(quick, 0, 4);
        var custom = Flow();
        _presets.Width = 220;
        _presets.DropDownStyle = ComboBoxStyle.DropDownList;
        _presets.AccessibleName = "Preset guardado";
        custom.Controls.Add(_presets);
        custom.Controls.Add(
            Button(
                "Aplicar",
                () =>
                {
                    if (_presets.SelectedItem is Preset p)
                        ApplyPreset(p.Profile);
                }
            )
        );
        custom.Controls.Add(Button("Guardar preset", SavePreset));
        custom.Controls.Add(Button("Apagar", DeletePreset));
        root.Controls.Add(custom, 0, 5);

        root.Controls.Add(_scheduled, 0, 6);
        root.Controls.Add(_schedule, 0, 7);
        var scheduleEdit = Flow();
        scheduleEdit.Controls.Add(Label("Hora"));
        scheduleEdit.Controls.Add(_time);
        scheduleEdit.Controls.Add(Label("Transição"));
        scheduleEdit.Controls.Add(_transition);
        scheduleEdit.Controls.Add(Label("min"));
        scheduleEdit.Controls.Add(Button("Guardar hora", SaveTime));
        scheduleEdit.Controls.Add(Button("Remover", RemoveTime));
        root.Controls.Add(scheduleEdit, 0, 8);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(
            new Label
            {
                Text = "Candlelight 0.2 · versão de teste",
                AutoSize = true,
                ForeColor = Muted,
                Font = new("Segoe UI", 8),
            },
            0,
            1
        );
        root.Controls.Add(footer, 0, 9);
        StyleControls(this);
        UpdateDisplays(displays);

        _monitors.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || SelectedId is null)
                return;
            Change(settings => settings.SelectedMonitor = SelectedId, false);
            LoadMonitor();
            SelectionChanged?.Invoke();
        };
        _enabled.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                EditMonitor(m => m.Enabled = _enabled.Checked);
        };
        _filterCursor.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                Change(settings => settings.FilterCursor = _filterCursor.Checked);
        };
        _scheduled.CheckedChanged += (_, _) =>
        {
            if (!_loading)
                EditMonitor(m => m.Scheduled = _scheduled.Checked);
        };
        _red.CheckedChanged += (_, _) =>
        {
            SetTemperatureEnabled();
            ManualChanged();
        };
        _temperature.ValueChanged += (_, _) =>
        {
            _temperatureSlider.Value = (int)_temperature.Value;
            ManualChanged();
        };
        _brightness.ValueChanged += (_, _) =>
        {
            _brightnessSlider.Value = (int)_brightness.Value;
            ManualChanged();
        };
        _temperatureSlider.ValueChanged += (_, _) => _temperature.Value = _temperatureSlider.Value;
        _brightnessSlider.ValueChanged += (_, _) => _brightness.Value = _brightnessSlider.Value;
        _schedule.SelectedIndexChanged += (_, _) =>
        {
            if (_schedule.SelectedItem is ScheduleChoice point)
            {
                _time.Text = point.Point.Time.ToString("HH:mm");
                _transition.Value = point.Point.TransitionMinutes;
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
        LoadMonitor();
        // Controls above are expressed in 96-DPI layout units. Font assignment can
        // autoscale an empty form early; establish design dimensions after layout.
        AutoScaleDimensions = new(96, 96);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var enabled = 1;
        DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }

    public void UpdateDisplays(IEnumerable<DisplayDescriptor> displays)
    {
        var live = displays.ToDictionary(d => d.Id);
        var settings = _store.Read();
        var selected = SelectedId ?? settings.SelectedMonitor;
        var choices = settings
            .Monitors.Select(m => new MonitorChoice(
                m.Id,
                m.Name + (live.ContainsKey(m.Id) ? "" : " (desligado)")
            ))
            .ToArray();
        if (_monitors.Items.Cast<MonitorChoice>().SequenceEqual(choices))
            return;
        _loading = true;
        _monitors.Items.Clear();
        _monitors.Items.AddRange(choices);
        _monitors.SelectedIndex =
            choices.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(choices, c => c.Id == selected));
        _loading = false;
        LoadMonitor();
    }

    public void ShowStatus(EngineSnapshot snapshot)
    {
        if (IsDisposed)
            return;
        UpdateDisplays(snapshot.Monitors.Select(m => m.Display));
        var state = snapshot.Monitors.FirstOrDefault(m => m.Display.Id == SelectedId);
        var color =
            state?.Profile.Mode == ColorMode.PureRed
                ? "Vermelho puro"
                : $"{state?.Profile.Temperature:0} K";
        _status.Text =
            _controller?.Error
            ?? snapshot.CursorError
            ?? state?.Error
            ?? (
                state is null ? "Monitor desligado; perfil guardado."
                : !state.Enabled ? "Filtro em pausa neste monitor."
                : state.Black ? "A retomar o monitor…"
                : snapshot.DesktopGain is not null && !snapshot.DesktopEffectVerified
                    ? "Não foi possível confirmar o filtro de cor."
                : snapshot.Renderer == "PerMonitorWindows"
                    ? "Perfis separados: a barra de tarefas pode ficar sem filtro."
                : !snapshot.UiAccessEnabled
                && snapshot.DesktopGain is { } shared
                && ChannelGain.FromProfile(state.Profile) != shared
                    ? $"Ativo · {color} · menus do Windows com cobertura limitada."
                : $"Ativo · {color} · {state.Profile.Brightness:P0}"
            );
    }

    private ColorProfile ReadColor() =>
        new(
            _red.Checked ? ColorMode.PureRed : ColorMode.Temperature,
            (double)_temperature.Value,
            (double)_brightness.Value / 100
        );

    private void ManualChanged()
    {
        if (_loading)
            return;
        EditMonitor(m =>
        {
            m.Manual = ReadColor();
            m.Scheduled = false;
        });
        _loading = true;
        _scheduled.Checked = false;
        _loading = false;
    }

    public void ApplyPreset(ColorProfile profile)
    {
        EditMonitor(m =>
        {
            m.Manual = profile;
            m.Scheduled = false;
            m.Enabled = true;
        });
        LoadMonitor();
    }

    private void LoadMonitor()
    {
        var settings = _store.Read();
        var profile = settings.Monitors.FirstOrDefault(m => m.Id == SelectedId);
        _loading = true;
        _filterCursor.Checked = settings.FilterCursor;
        _loading = false;
        if (profile is null)
            return;
        _loading = true;
        _enabled.Checked = profile.Enabled;
        _scheduled.Checked = profile.Scheduled;
        _red.Checked = profile.Manual.Mode == ColorMode.PureRed;
        _temperature.Value = (decimal)profile.Manual.Temperature;
        _brightness.Value = (decimal)Math.Round(profile.Manual.Brightness * 100);
        _temperatureSlider.Value = (int)_temperature.Value;
        _brightnessSlider.Value = (int)_brightness.Value;
        _presets.Items.Clear();
        _presets.Items.AddRange(settings.Presets.ToArray());
        _presets.DisplayMember = nameof(Preset.Name);
        if (_presets.Items.Count > 0)
            _presets.SelectedIndex = 0;
        _schedule.Items.Clear();
        _schedule.Items.AddRange(
            profile.Schedule.OrderBy(p => p.Time).Select(p => new ScheduleChoice(p)).ToArray()
        );
        SetTemperatureEnabled();
        _loading = false;
    }

    private void SetTemperatureEnabled() =>
        _temperature.Enabled = _temperatureSlider.Enabled = !_red.Checked;

    public void Reload() => LoadMonitor();

    internal void VerifyPreviewControls()
    {
        var other = _store.Read().Monitors.Single(m => m.Id != SelectedId).Manual;
        _brightness.Value = 100;
        _temperature.Value = 4000;
        if (_store.Read().Monitors.Single(m => m.Id == SelectedId).Manual.Temperature != 4000)
            throw new InvalidOperationException("Numeric temperature binding failed.");
        _temperature.Value = 2700;
        ApplyPreset(new(ColorMode.PureRed, 500, 0.15));
        var red = _store.Read().Monitors.Single(m => m.Id == SelectedId);
        if (
            red.Manual.Mode != ColorMode.PureRed
            || red.Manual.Brightness != 0.15
            || !_red.Checked
            || _temperature.Enabled
        )
            throw new InvalidOperationException("Red preset binding failed.");
        if (_store.Read().Monitors.Single(m => m.Id != SelectedId).Manual != other)
            throw new InvalidOperationException("Preset changed another monitor.");
        _time.Text = "22:00";
        SaveTime();
        _scheduled.Checked = true;
        if (!_store.Read().Monitors.Single(m => m.Id == SelectedId).Scheduled)
            throw new InvalidOperationException("Schedule binding failed.");
        ApplyPreset(new(ColorMode.Temperature, 2700, 1));
        _filterCursor.Checked = false;
        Reload();
        if (_filterCursor.Checked || _store.Read().FilterCursor)
            throw new InvalidOperationException("Cursor option binding failed.");
        _filterCursor.Checked = true;
    }

    private void EditMonitor(Action<MonitorProfile> edit) =>
        Change(settings =>
        {
            var monitor = settings.Monitors.FirstOrDefault(m => m.Id == SelectedId);
            if (monitor is not null)
                edit(monitor);
        });

    private async void Change(Action<AppSettings> edit, bool apply = true)
    {
        try
        {
            _store.Update(edit);
            if (apply && _controller is not null)
                await _controller.ApplyNowAsync();
        }
        catch (Exception error)
        {
            _status.Text = "Não foi possível aplicar: " + error.Message;
        }
    }

    private void SaveTime()
    {
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
            _status.Text = "Escreve uma hora no formato HH:mm.";
            return;
        }
        EditMonitor(m =>
        {
            m.Schedule.RemoveAll(p => p.Time == time);
            m.Schedule.Add(new(time, ReadColor(), (int)_transition.Value));
        });
        LoadMonitor();
        _status.Text = "Hora guardada com a cor e o brilho escolhidos acima.";
    }

    private void RemoveTime()
    {
        if (_schedule.SelectedItem is not ScheduleChoice choice)
            return;
        EditMonitor(m => m.Schedule.RemoveAll(p => p.Time == choice.Point.Time));
        LoadMonitor();
    }

    private void SavePreset()
    {
        using var prompt = new Form
        {
            Text = "Guardar preset",
            BackColor = Surface,
            ForeColor = TextColor,
            Font = Font,
            ClientSize = new(350, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var name = new TextBox
        {
            Left = 16,
            Top = 16,
            Width = 318,
            BackColor = Field,
            ForeColor = TextColor,
            AccessibleName = "Nome do preset",
        };
        var save = Button(
            "Guardar",
            () =>
            {
                if (!string.IsNullOrWhiteSpace(name.Text))
                    prompt.DialogResult = DialogResult.OK;
            }
        );
        save.Location = new(230, 58);
        prompt.Controls.AddRange([name, save]);
        prompt.AcceptButton = save;
        if (prompt.ShowDialog(this) != DialogResult.OK)
            return;
        Change(
            settings =>
            {
                settings.Presets.RemoveAll(p =>
                    p.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                );
                settings.Presets.Add(new(name.Text.Trim(), ReadColor()));
            },
            false
        );
        LoadMonitor();
    }

    private void DeletePreset()
    {
        if (_presets.SelectedItem is not Preset preset)
            return;
        Change(s => s.Presets.RemoveAll(p => p.Name == preset.Name), false);
        LoadMonitor();
    }

    private static Label Label(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Margin = new(0, 6, 6, 0),
        };

    private static FlowLayoutPanel Flow() =>
        new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new(0),
            Padding = new(0),
        };

    private static FlowLayoutPanel WithUnit(Control input, string unit)
    {
        var panel = Flow();
        panel.Controls.Add(input);
        panel.Controls.Add(Label(unit));
        return panel;
    }

    private static Button Button(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Padding = new(8, 2, 8, 2),
            Margin = new(0, 0, 8, 0),
            BackColor = Field,
            ForeColor = TextColor,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(66, 80, 76);
        button.Click += (_, _) => click();
        return button;
    }

    private static void StyleControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TextBox or ComboBox or NumericUpDown or ListBox)
            {
                control.BackColor = Field;
                control.ForeColor = TextColor;
            }
            if (control is ComboBox combo)
            {
                combo.FlatStyle = FlatStyle.Flat;
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.ItemHeight = 22;
                combo.DrawItem += (_, e) =>
                {
                    using var background = new SolidBrush(Field);
                    e.Graphics.FillRectangle(background, e.Bounds);
                    if (e.Index >= 0)
                        TextRenderer.DrawText(
                            e.Graphics,
                            combo.GetItemText(combo.Items[e.Index]),
                            e.Font,
                            e.Bounds,
                            TextColor,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                        );
                    if ((e.State & DrawItemState.Focus) != 0)
                        e.DrawFocusRectangle();
                };
            }
            StyleControls(control);
        }
    }

    private sealed record MonitorChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ScheduleChoice(SchedulePoint Point)
    {
        public override string ToString() =>
            $"{Point.Time:HH:mm}     {(Point.Profile.Mode == ColorMode.PureRed ? "Vermelho puro" : $"{Point.Profile.Temperature:0} K")}     {Point.Profile.Brightness:P0}     {Point.TransitionMinutes} min";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int size
    );
}
