using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightBulb.Core;

namespace LightBulb.ViewModels.Components;

public partial class DisplayControlsViewModel
{
    private DateTimeOffset _lastSolarInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NormalColorsLabel))]
    [NotifyPropertyChangedFor(nameof(NormalColorsNotice))]
    public partial bool IsNormalColors { get; set; }
    public string NormalColorsLabel => IsNormalColors ? "Retomar ajustes" : "Cores normais";
    public string NormalColorsNotice =>
        IsNormalColors
            ? "Ajustes do Candlelight suspensos neste ecrã. A Luz noturna do Windows pode continuar ativa. Retomar volta ao programa guardado à hora atual."
            : "";

    [ObservableProperty]
    public partial double SunriseMinutes { get; set; } = 120;

    [ObservableProperty]
    public partial double SunsetMinutes { get; set; } = 120;

    [ObservableProperty]
    public partial TransitionCurve SunriseCurve { get; set; } = TransitionCurve.Smooth;

    [ObservableProperty]
    public partial TransitionCurve SunsetCurve { get; set; } = TransitionCurve.Smooth;

    [ObservableProperty]
    public partial string SunriseRange { get; set; } = "";

    [ObservableProperty]
    public partial string SunsetRange { get; set; } = "";

    [ObservableProperty]
    public partial string SolarStatus { get; set; } = "";

    [ObservableProperty]
    public partial string LocationQuery { get; set; } = "";

    [ObservableProperty]
    public partial string LocationFeedback { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveSolarLocationCommand))]
    public partial bool IsResolvingLocation { get; set; }

    private SolarCycleOptions EditedSolarOptions =>
        new()
        {
            SunriseMinutes = SolarCycleOptions.SafeDuration(SunriseMinutes),
            SunsetMinutes = SolarCycleOptions.SafeDuration(SunsetMinutes),
            SunriseCurve = (SunriseCurve ?? TransitionCurve.Smooth).Normalize(),
            SunsetCurve = (SunsetCurve ?? TransitionCurve.Smooth).Normalize(),
        };

    private static bool IsSolarEditorProperty(string? name) =>
        name
            is nameof(IsNormalColors)
                or nameof(SunriseMinutes)
                or nameof(SunsetMinutes)
                or nameof(SunriseCurve)
                or nameof(SunsetCurve);

    private void LoadSolarEditor(DisplayProfile profile)
    {
        var solar = profile.SolarCycle ?? new SolarCycleOptions();
        IsNormalColors = profile.IsNormalColors;
        SunriseMinutes = SolarCycleOptions.SafeDuration(solar.SunriseMinutes);
        SunsetMinutes = SolarCycleOptions.SafeDuration(solar.SunsetMinutes);
        SunriseCurve = (solar.SunriseCurve ?? TransitionCurve.Smooth).Normalize();
        SunsetCurve = (solar.SunsetCurve ?? TransitionCurve.Smooth).Normalize();
        if (string.IsNullOrWhiteSpace(LocationQuery))
            LocationQuery = _settings.Location?.ToString() ?? "";
        _lastSolarInfo = default;
        UpdateSolarInfo(DateTimeOffset.Now, true);
    }

    private void UpdateSolarInfo(DateTimeOffset instant, bool force = false)
    {
        // Evaluation runs frequently; captions only need minute-level refreshes.
        if (!force && instant.ToUnixTimeSeconds() / 60 == _lastSolarInfo.ToUnixTimeSeconds() / 60)
            return;
        _lastSolarInfo = instant;
        var date = DateOnly.FromDateTime(instant.LocalDateTime);
        var location = _settings.IsManualSunriseSunsetEnabled ? null : _settings.Location;
        var fallback = new SolarTimes(_settings.ManualSunrise, _settings.ManualSunset);
        var (rise, set) = SolarCycle.Events(date, location, fallback, TimeZoneInfo.Local);
        SunriseRange =
            $"{rise:HH:mm} → {rise.AddMinutes(SolarCycleOptions.SafeDuration(SunriseMinutes)):HH:mm}";
        SunsetRange =
            $"{set.AddMinutes(-SolarCycleOptions.SafeDuration(SunsetMinutes)):HH:mm} → {set:HH:mm}";
        SolarStatus = location is not null
            ? $"Hoje, {instant.LocalDateTime:dd MMM} · horas calculadas diariamente para a localização abaixo."
            : "Horas fixas: define a localização abaixo para acompanhar automaticamente o sol.";
    }

    partial void OnSunriseMinutesChanged(double value) => UpdateSolarInfo(DateTimeOffset.Now, true);

    partial void OnSunsetMinutesChanged(double value) => UpdateSolarInfo(DateTimeOffset.Now, true);

    private bool CanResolveSolarLocation() => !IsResolvingLocation;

    [RelayCommand(CanExecute = nameof(CanResolveSolarLocation))]
    private async Task ResolveSolarLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(LocationQuery))
        {
            LocationFeedback = "Escreve uma cidade ou latitude, longitude.";
            return;
        }
        IsResolvingLocation = true;
        try
        {
            var location =
                GeoLocation.TryParse(LocationQuery) ?? await GeoLocation.SearchAsync(LocationQuery);
            if (
                !double.IsFinite(location.Latitude)
                || !double.IsFinite(location.Longitude)
                || Math.Abs(location.Latitude) > 90
                || Math.Abs(location.Longitude) > 180
            )
                throw new ArgumentException("Invalid coordinates.");
            _settings.Location = location;
            _settings.IsManualSunriseSunsetEnabled = false;
            _dirty = true;
            SaveNow();
            UpdateSolarInfo(DateTimeOffset.Now, true);
            ProfilesChanged?.Invoke();
            LocationFeedback = $"Localização guardada: {location}. Partilhada por todos os ecrãs.";
        }
        catch
        {
            LocationFeedback =
                "Não foi possível encontrar essa localização. Usa cidade e país, ou latitude, longitude.";
        }
        finally
        {
            IsResolvingLocation = false;
        }
    }

    private static TransitionCurve PresetCurve(string name) =>
        name switch
        {
            "linear" => TransitionCurve.Linear,
            "early" => TransitionCurve.Early,
            "late" => TransitionCurve.Late,
            _ => TransitionCurve.Smooth,
        };

    [RelayCommand]
    private void SetSunriseCurve(string name) => SunriseCurve = PresetCurve(name);

    [RelayCommand]
    private void SetSunsetCurve(string name) => SunsetCurve = PresetCurve(name);
}
