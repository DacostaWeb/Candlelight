using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightBulb.Core;
using LightBulb.Framework;
using LightBulb.PlatformInterop;
using LightBulb.Services;

namespace LightBulb.ViewModels.Components;

public partial class DisplayItemViewModel(DisplayInfo info, string name) : ObservableObject
{
    public DisplayInfo Info { get; } = info;
    public string Id => Info.Id;

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Readout))]
    public partial ColorConfiguration Current { get; set; }
    public string Details =>
        $"{(Info.IsPrimary ? "Principal · " : "")}{Info.Bounds.Right - Info.Bounds.Left} × {Info.Bounds.Bottom - Info.Bounds.Top}";
    public string Readout => $"{Current.Temperature:0} K · {Current.Brightness:P0}";
}

public partial class SchedulePointViewModel : ObservableObject
{
    public SchedulePointViewModel(SchedulePoint point)
    {
        Time = point.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        Temperature = point.Configuration.Temperature;
        Brightness = point.Configuration.Brightness * 100;
        TransitionMinutes = point.Transition.TotalMinutes;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Time { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial double TransitionMinutes { get; set; }

    public bool IsValid =>
        TimeOnly.TryParseExact(
            Time,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _
        )
        && double.IsFinite(Temperature)
        && Temperature is >= 500 and <= 20000
        && double.IsFinite(Brightness)
        && Brightness is >= 1 and <= 100
        && double.IsFinite(TransitionMinutes)
        && TransitionMinutes is >= 0 and <= 1440;

    public SchedulePoint ToPoint() =>
        new(
            TimeOnly.ParseExact(Time, "HH:mm", CultureInfo.InvariantCulture),
            new ColorConfiguration(Temperature, Brightness / 100),
            TimeSpan.FromMinutes(TransitionMinutes)
        );
}

public partial class DisplayControlsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly GammaService _gamma;
    private readonly DispatcherTimer _saveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };
    private bool _loading;
    private bool _initialized;
    private bool _dirty;
    private readonly Dictionary<string, ColorConfiguration> _hotkeyOrigins = new();

    public DisplayControlsViewModel(SettingsService settings, GammaService gamma)
    {
        _settings = settings;
        _gamma = gamma;
        _saveTimer.Tick += (_, _) => SaveNow();
        PropertyChanged += EditorChanged;
        _gamma.DisplaysChanged += OnDisplaysChanged;
        _gamma.ApplyStatusChanged += OnApplyStatusChanged;
        _settings.PropertyChanged += SettingsChanged;
    }

    public ObservableCollection<DisplayItemViewModel> Displays { get; } = [];
    public event Action? ProfilesChanged;
    public ObservableCollection<ColorPreset> Presets { get; } = [];
    public ObservableCollection<SchedulePointViewModel> Schedule { get; } = [];
    public IReadOnlyList<string> Modes { get; } = ["Dia / noite", "Manual", "Horários"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisplay))]
    public partial DisplayItemViewModel? SelectedDisplay { get; set; }
    public bool HasDisplay => SelectedDisplay is not null;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDisplayEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    [NotifyPropertyChangedFor(nameof(IsSunCycle))]
    [NotifyPropertyChangedFor(nameof(IsSchedule))]
    public partial int ModeIndex { get; set; }
    public bool IsManual => ModeIndex == (int)DisplayControlMode.Manual;
    public bool IsSunCycle => ModeIndex == (int)DisplayControlMode.SunCycle;
    public bool IsSchedule => ModeIndex == (int)DisplayControlMode.Schedule;

    [ObservableProperty]
    public partial double Temperature { get; set; } = 6600;

    [ObservableProperty]
    public partial double Brightness { get; set; } = 100;

    [ObservableProperty]
    public partial double DayTemperature { get; set; } = 6600;

    [ObservableProperty]
    public partial double DayBrightness { get; set; } = 100;

    [ObservableProperty]
    public partial double NightTemperature { get; set; } = 3900;

    [ObservableProperty]
    public partial double NightBrightness { get; set; } = 85;

    [ObservableProperty]
    public partial string PresetName { get; set; } = "";

    [ObservableProperty]
    public partial string Feedback { get; set; } = "";

    [ObservableProperty]
    public partial string ScheduleError { get; set; } = "";

    [ObservableProperty]
    public partial string DriverStatus { get; set; } = "";

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsWakeRecoveryEnabled { get; set; } = true;

    public string PreviewNotice =>
        StartOptions.Current.IsPreview ? "Pré-visualização · os ecrãs não são alterados" : "";

    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        IsWakeRecoveryEnabled = _settings.IsWakeRecoveryEnabled;
        RefreshPresets();
        RefreshDisplays();
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName
            is not (
                ""
                or nameof(SettingsService.Presets)
                or nameof(SettingsService.DisplayProfiles)
            )
        )
            return;
        if (!_initialized || _loading)
            return;
        RefreshPresets();
        // A settings reset must also refresh the editor, without overwriting new settings.
        if (args.PropertyName == "" && !_dirty)
        {
            if (Displays.Any(d => _settings.DisplayProfiles.All(p => p.Id != d.Id)))
                RefreshDisplays();
            else
                LoadEditor();
        }
    }

    private void OnDisplaysChanged() => Dispatcher.UIThread.Post(RefreshDisplays);

    private void OnApplyStatusChanged() => Dispatcher.UIThread.Post(UpdateDriverStatus);

    [RelayCommand]
    private void Reapply() => _gamma.Recover();

    [RelayCommand]
    private void RefreshDisplays()
    {
        SaveNow();
        var selectedId = SelectedDisplay?.Id ?? _settings.SelectedDisplayId;
        _loading = true;
        try
        {
            Displays.Clear();
            foreach (var info in _gamma.GetDisplays().OrderByDescending(d => d.IsPrimary))
            {
                var profile = _settings.DisplayProfiles.FirstOrDefault(p =>
                    p.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase)
                );
                if (profile is null)
                {
                    profile = new DisplayProfile
                    {
                        Id = info.Id,
                        Name = info.Name,
                        DayConfiguration = _settings.DayConfiguration,
                        NightConfiguration = _settings.NightConfiguration,
                        ManualConfiguration = _settings.NightConfiguration,
                    };
                    _settings.DisplayProfiles = [.. _settings.DisplayProfiles, profile];
                    _dirty = true;
                }
                Displays.Add(new DisplayItemViewModel(info, profile.Name));
            }
            SelectedDisplay =
                Displays.FirstOrDefault(d => d.Id == selectedId) ?? Displays.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
        LoadEditor();
        UpdateDriverStatus();
        QueueSave();
        ProfilesChanged?.Invoke();
    }

    partial void OnSelectedDisplayChanged(DisplayItemViewModel? value)
    {
        if (_loading)
            return;
        LoadEditor();
        _settings.SelectedDisplayId = value?.Id;
        _dirty = true;
        QueueSave();
    }

    private void LoadEditor()
    {
        _loading = true;
        try
        {
            IsWakeRecoveryEnabled = _settings.IsWakeRecoveryEnabled;
            foreach (var point in Schedule)
                point.PropertyChanged -= ScheduleChanged;
            Schedule.Clear();
            var profile = _settings.DisplayProfiles.FirstOrDefault(p =>
                p.Id == SelectedDisplay?.Id
            );
            if (profile is null)
                return;
            DisplayName = profile.Name;
            IsDisplayEnabled = profile.IsEnabled;
            ModeIndex = (int)profile.Mode;
            Temperature = profile.ManualConfiguration.Temperature;
            Brightness = profile.ManualConfiguration.Brightness * 100;
            DayTemperature = profile.DayConfiguration.Temperature;
            DayBrightness = profile.DayConfiguration.Brightness * 100;
            NightTemperature = profile.NightConfiguration.Temperature;
            NightBrightness = profile.NightConfiguration.Brightness * 100;
            foreach (var point in profile.Schedule.OrderBy(p => p.Time))
                AddRow(point);
            ScheduleError = "";
            Feedback = "";
        }
        finally
        {
            _loading = false;
        }
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_loading || !_initialized)
            return;
        if (args.PropertyName == nameof(IsWakeRecoveryEnabled))
        {
            _settings.IsWakeRecoveryEnabled = IsWakeRecoveryEnabled;
            _dirty = true;
            QueueSave();
        }
        else if (
            args.PropertyName
            is nameof(DisplayName)
                or nameof(IsDisplayEnabled)
                or nameof(ModeIndex)
                or nameof(Temperature)
                or nameof(Brightness)
                or nameof(DayTemperature)
                or nameof(DayBrightness)
                or nameof(NightTemperature)
                or nameof(NightBrightness)
        )
            PersistEditor();
    }

    private void ScheduleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SchedulePointViewModel.IsValid))
            PersistEditor();
    }

    private void PersistEditor()
    {
        if (_loading || SelectedDisplay is null)
            return;
        var old = _settings.DisplayProfiles.First(p => p.Id == SelectedDisplay.Id);
        var points = old.Schedule.ToArray();
        ScheduleError = "";
        if (Schedule.Any(p => !p.IsValid))
        {
            ScheduleError =
                "Usa horas HH:mm, 500–20000 K, brilho 1–100% e transição 0–1440 min. O último horário válido continua ativo.";
        }
        else
        {
            var candidate = Schedule.Select(p => p.ToPoint()).OrderBy(p => p.Time).ToArray();
            if (candidate.Select(p => p.Time).Distinct().Count() != candidate.Length)
                ScheduleError =
                    "Cada horário precisa de uma hora diferente. O último horário válido continua ativo.";
            else
                points = candidate;
        }
        if (ModeIndex == 2 && points.Length == 0 && ScheduleError.Length == 0)
        {
            ScheduleError =
                "Adiciona um horário ou usa o exemplo diário. Até lá mantém os valores manuais.";
        }

        ColorConfiguration Config(double temperature, double brightness) =>
            new ColorConfiguration(temperature, brightness / 100).Clamp(500, 20000, 0.01, 1);
        var profile = old with
        {
            Name = string.IsNullOrWhiteSpace(DisplayName)
                ? SelectedDisplay.Info.Name
                : DisplayName.Trim(),
            IsEnabled = IsDisplayEnabled,
            Mode = (DisplayControlMode)Math.Clamp(ModeIndex, 0, 2),
            ManualConfiguration = Config(Temperature, Brightness),
            DayConfiguration = Config(DayTemperature, DayBrightness),
            NightConfiguration = Config(NightTemperature, NightBrightness),
            Schedule = points,
        };
        _loading = true;
        try
        {
            SelectedDisplay.Name = profile.Name;
            _settings.DisplayProfiles = _settings
                .DisplayProfiles.Select(p => p.Id == profile.Id ? profile : p)
                .ToArray();
        }
        finally
        {
            _loading = false;
        }
        _dirty = true;
        QueueSave();
        ProfilesChanged?.Invoke();
    }

    private void AddRow(SchedulePoint point)
    {
        var row = new SchedulePointViewModel(point);
        row.PropertyChanged += ScheduleChanged;
        Schedule.Add(row);
    }

    [RelayCommand]
    private void AddSchedulePoint()
    {
        var used = Schedule.Where(p => p.IsValid).Select(p => p.ToPoint().Time).ToHashSet();
        var time = new TimeOnly(12, 0);
        for (var i = 0; used.Contains(time) && i < 1440; i++)
            time = time.AddMinutes(1);
        if (used.Contains(time))
            return;
        AddRow(
            new SchedulePoint(
                time,
                new ColorConfiguration(Temperature, Brightness / 100),
                TimeSpan.FromMinutes(30)
            )
        );
        PersistEditor();
    }

    [RelayCommand]
    private void RemoveSchedulePoint(SchedulePointViewModel point)
    {
        point.PropertyChanged -= ScheduleChanged;
        Schedule.Remove(point);
        PersistEditor();
    }

    [RelayCommand]
    private void UseExampleSchedule()
    {
        _loading = true;
        foreach (var point in Schedule)
            point.PropertyChanged -= ScheduleChanged;
        Schedule.Clear();
        AddRow(new(new TimeOnly(7, 0), new(6600, 1), TimeSpan.FromMinutes(30)));
        AddRow(new(new TimeOnly(18, 0), new(2700, 0.65), TimeSpan.FromMinutes(60)));
        AddRow(new(new TimeOnly(22, 0), new(500, 0.15), TimeSpan.FromMinutes(30)));
        ModeIndex = 2;
        _loading = false;
        PersistEditor();
    }

    [RelayCommand]
    public void ApplyPreset(ColorPreset preset)
    {
        if (SelectedDisplay is null)
            return;
        _loading = true;
        Temperature = preset.Configuration.Temperature;
        Brightness = preset.Configuration.Brightness * 100;
        ModeIndex = 1;
        IsDisplayEnabled = true;
        _loading = false;
        PersistEditor();
        Feedback =
            $"{preset.Name} aplicado a {SelectedDisplay.Name}. Mantém-se até escolheres outro modo.";
    }

    [RelayCommand]
    private void ApplyPresetById(string id)
    {
        if (_settings.Presets.FirstOrDefault(p => p.Id == id) is { } preset)
            ApplyPreset(preset);
    }

    public void AdjustSelected(double temperatureDelta, double brightnessDelta)
    {
        if (SelectedDisplay is null)
            return;
        var current = SelectedDisplay.Current.Clamp(500, 20000, 0.01, 1);
        _hotkeyOrigins.TryAdd(SelectedDisplay.Id, current);
        ApplyPreset(
            new ColorPreset(
                "adjustment",
                "Ajuste por atalho",
                current.WithOffset(temperatureDelta, brightnessDelta).Clamp(500, 20000, 0.01, 1)
            )
        );
    }

    public void ResetSelectedAdjustments()
    {
        if (
            SelectedDisplay is not null
            && _hotkeyOrigins.Remove(SelectedDisplay.Id, out var original)
        )
            ApplyPreset(new ColorPreset("reset", "Valores anteriores", original));
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0 || SelectedDisplay is null)
        {
            Feedback = "Dá um nome ao preset antes de guardar.";
            return;
        }
        var configuration = IsManual
            ? new ColorConfiguration(Temperature, Brightness / 100)
            : SelectedDisplay.Current;
        var old = _settings.Presets.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        );
        var preset = new ColorPreset(
            old?.Id ?? Guid.NewGuid().ToString("N"),
            name,
            configuration.Clamp(500, 20000, 0.01, 1)
        );
        _settings.Presets = old is null
            ? [.. _settings.Presets, preset]
            : _settings.Presets.Select(p => p.Id == old.Id ? preset : p).ToArray();
        _dirty = true;
        QueueSave();
        Feedback =
            $"Preset «{name}» guardado com {configuration.Temperature:0} K e {configuration.Brightness:P0}.";
        PresetName = "";
    }

    [RelayCommand]
    private void DeletePreset(ColorPreset preset)
    {
        _settings.Presets = _settings.Presets.Where(p => p.Id != preset.Id).ToArray();
        _dirty = true;
        QueueSave();
    }

    private void RefreshPresets()
    {
        Presets.Clear();
        foreach (var preset in _settings.Presets)
            Presets.Add(preset);
    }

    public IReadOnlyDictionary<string, ColorConfiguration> Evaluate(
        SolarTimes solarTimes,
        DateTimeOffset instant,
        bool active,
        double temperatureOffset,
        double brightnessOffset
    )
    {
        var configurations = new Dictionary<string, ColorConfiguration>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var profile in _settings.DisplayProfiles)
        {
            var config =
                active
                    ? profile.Evaluate(
                        solarTimes,
                        _settings.ConfigurationTransitionDuration,
                        _settings.ConfigurationTransitionOffset,
                        instant
                    )
                : _settings.IsDefaultToDayConfigurationEnabled ? profile.DayConfiguration
                : ColorConfiguration.Default;
            if (active && profile.IsEnabled && profile.Id == SelectedDisplay?.Id)
                config = config.WithOffset(temperatureOffset, brightnessOffset);
            configurations[profile.Id] = config.Clamp(500, 20000, 0.01, 1);
        }
        foreach (var display in Displays)
            display.Current = configurations.GetValueOrDefault(
                display.Id,
                ColorConfiguration.Default
            );
        return configurations;
    }

    private void UpdateDriverStatus() =>
        DriverStatus =
            _gamma.FailedDisplayIds.Count == 0
                ? ""
                : string.Join(
                    "\n",
                    _gamma.FailedDisplayIds.Select(id =>
                        $"{Displays.FirstOrDefault(d => d.Id == id)?.Name ?? id}: {_gamma.FailureReasons.GetValueOrDefault(id, "O Windows não aplicou a cor.")}"
                    )
                );

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        try
        {
            _settings.Save();
            _dirty = false;
            SaveStatus = "Guardado automaticamente";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Não foi possível guardar: {ex.Message}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SaveNow();
            _gamma.DisplaysChanged -= OnDisplaysChanged;
            _gamma.ApplyStatusChanged -= OnApplyStatusChanged;
            _settings.PropertyChanged -= SettingsChanged;
            foreach (var point in Schedule)
                point.PropertyChanged -= ScheduleChanged;
        }
        base.Dispose(disposing);
    }
}
