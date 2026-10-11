using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed partial class ControlWindow
{
    private bool _todayMode = true;
    private Button _todayTab = null!;
    private Button _programTab = null!;
    private Button _restoreProgram = null!;
    private Button _curveButton = null!;
    private readonly Label _scopeHint = LightTheme.Label("", 9, true);
    private DateTimeOffset? _observedExpiry;

    private AppSettings ReadView()
    {
        var settings = _store.Read();
        return _todayMode ? ScheduleSession.Effective(settings, DateTimeOffset.Now) : settings;
    }

    private Control BuildScopeRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new(0),
        };
        row.ColumnStyles.Add(new(SizeType.Absolute, 200));
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.Absolute, 175));
        row.RowStyles.Add(new(SizeType.Percent, 100));
        var tabs = LightTheme.Row();
        _todayTab = LightTheme.Button("Só hoje", () => SetScope(true));
        _programTab = LightTheme.Button("Programa", () => SetScope(false));
        tabs.Controls.Add(_todayTab);
        tabs.Controls.Add(_programTab);
        row.Controls.Add(tabs, 0, 0);
        _scopeHint.AutoSize = false;
        _scopeHint.AutoEllipsis = true;
        _scopeHint.Dock = DockStyle.Fill;
        _scopeHint.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(_scopeHint, 1, 0);
        _restoreProgram = LightTheme.Button("Retomar programa", RestoreProgram);
        _restoreProgram.Anchor = AnchorStyles.Right;
        row.Controls.Add(_restoreProgram, 2, 0);
        return row;
    }

    private void SetScope(bool today)
    {
        if (_todayMode != today)
            _history.Clear();
        _todayMode = today;
        _editingManual = false;
        if (today)
        {
            _date = Today();
            _followingToday = true;
        }
        Reload();
    }

    private void UpdateScope()
    {
        var saved = _store.Read();
        var active = ScheduleSession.IsActive(saved, DateTimeOffset.Now);
        _observedExpiry = active ? saved.Today!.ExpiresAt : null;
        _todayTab.BackColor = _todayMode ? LightTheme.Field : LightTheme.Background;
        _programTab.BackColor = !_todayMode ? LightTheme.Field : LightTheme.Background;
        _todayTab.ForeColor = _todayMode ? LightTheme.Accent : TextColor;
        _programTab.ForeColor = !_todayMode ? LightTheme.Accent : TextColor;
        _scopeHint.Text = _todayMode
            ? active
                ? "Ajuste temporário ativo · termina às 00:00"
                : "Experimenta livremente · o programa fica intacto"
            : active
                ? "Editas o programa · o ajuste de hoje continua ativo"
                : "Estas alterações repetem-se todos os dias";
        _restoreProgram.Visible = active;
    }

    private bool ExpireTemporaryUi()
    {
        var expired = _store.ExpireToday(DateTimeOffset.Now);
        if (!expired && !(_observedExpiry is { } until && DateTimeOffset.Now >= until))
            return false;
        _observedExpiry = null;
        _history.Clear();
        _editingManual = false;
        if (_followingToday)
            _date = Today();
        Reload();
        _ = ApplyAsync();
        return true;
    }

    internal void RestoreProgram()
    {
        _history.Clear();
        _editingManual = false;
        Commit(s => s.Today = null, remember: false, permanent: true);
        Message("Programa retomado. Os ajustes de hoje foram descartados.");
    }

    private void EditCurve()
    {
        var settings = ReadView();
        var point = CurrentPoint(settings);
        if (point is null || _editingManual)
            return;
        using var dialog = new TransitionDialog(
            point,
            settings.Timing,
            _todayMode ? "Só hoje" : "Programa"
        );
        if (dialog.ShowDialog(this) == DialogResult.OK)
            EditTiming(_ => dialog.Result);
    }

    private void EditSolarCycle()
    {
        if (ReadView().Timing.Location is null && !ChooseLocation())
            return;
        var settings = ReadView();
        using var dialog = new SolarCycleDialog(
            settings.Timing,
            Current(settings),
            _date,
            _todayMode ? "Só hoje" : "Programa"
        );
        if (dialog.ShowDialog(this) != DialogResult.OK || _selectedMonitor is null)
            return;
        if (
            Commit(s =>
                ScheduleEditing.ApplySolarCycle(s, _selectedMonitor, dialog.Morning, dialog.Evening)
            )
        )
        {
            _selectedPoint = dialog.Evening.Id;
            _editingManual = false;
            Reload();
            Message(
                _todayMode
                    ? "Ciclo aplicado só hoje. À meia-noite volta o programa."
                    : "Ciclo solar guardado. As horas acompanham o sol ao longo do ano."
            );
        }
    }

    private void ChoosePreviewDate()
    {
        using var dialog = new Form
        {
            Text = "Pré-visualizar outro dia",
            BackColor = Surface,
            ForeColor = TextColor,
            Font = Font,
            AutoScaleDimensions = new(96, 96),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new(350, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            Icon = Icon,
        };
        var date = new DateTimePicker
        {
            Left = 20,
            Top = 20,
            Width = 310,
            Format = DateTimePickerFormat.Long,
            Value = _date.ToDateTime(TimeOnly.MinValue),
        };
        var hint = LightTheme.Label("A prévia não altera a cor atual do ecrã.", 9, true);
        hint.Location = new(20, 58);
        var apply = LightTheme.Button("Ver este dia", () => dialog.DialogResult = DialogResult.OK);
        apply.Location = new(205, 100);
        dialog.Controls.AddRange([date, hint, apply]);
        dialog.AcceptButton = apply;
        dialog.AutoScaleDimensions = new(96, 96);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _date = DateOnly.FromDateTime(date.Value);
        _followingToday = _date == Today();
        if (!_followingToday)
        {
            _todayMode = false;
            _history.Clear();
        }
        Reload();
    }

    private void VerifyTemporaryControls()
    {
        RestoreProgram();
        var baseline = System.Text.Json.JsonSerializer.Serialize(
            _store.Read().Timing,
            ProfileStore.Json
        );
        var baselineColors = new Dictionary<string, ColorProfile>(
            Current(_store.Read()).PointColors
        );
        SetScope(true);
        _selectedPoint = ReadView().Timing.Points[0].Id;
        _editingManual = false;
        Reload();
        _temperature.Value = 3500;
        EditTiming(p => p with { Curve = TransitionCurve.SlowStart, TransitionMinutes = 75 });
        if (_store.Read().Today is null || CurrentPoint(ReadView())!.TransitionMinutes != 75)
            throw new InvalidOperationException("Temporary point/curve binding failed.");
        if (
            System.Text.Json.JsonSerializer.Serialize(_store.Read().Timing, ProfileStore.Json)
                != baseline
            || baselineColors.Any(p => Current(_store.Read()).PointColors[p.Key] != p.Value)
        )
            throw new InvalidOperationException("A temporary change overwrote the program.");
        Undo();
        if (CurrentPoint(ReadView())!.TransitionMinutes == 75)
            throw new InvalidOperationException("Temporary curve undo failed.");
        SetScope(false);
        if (CurrentPoint(ReadView())!.TransitionMinutes == 75)
            throw new InvalidOperationException("Program scope shows the temporary curve.");
        RestoreProgram();
        SetScope(true);
    }
}
