using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed record Preset(string Name, ColorProfile Profile);

internal sealed record AppSettings
{
    public int Schema { get; set; } = 2;
    public bool FilterCursor { get; set; } = true;
    public SchedulePlan Timing { get; set; } = new();
    public List<MonitorProfile> Monitors { get; set; } = [];
    public List<Preset> Presets { get; set; } =
    [
        new("Normal", ColorProfile.Normal),
        new("Leitura", new(ColorMode.Temperature, 2700, 1)),
        new("Noite", new(ColorMode.Temperature, 1800, 0.35)),
        new("OLED vermelho", new(ColorMode.PureRed, 500, 0.15)),
    ];
    public string? SelectedMonitor { get; set; }
}

internal sealed class ProfileStore
{
    private readonly object _gate = new();
    private AppSettings _settings;
    public string Path { get; }
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProfileStore(string path, string? import = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        _settings = File.Exists(Path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json)
                ?? throw new InvalidDataException("Settings are empty.")
            : Import(import);
        var migrated = _settings.Schema == 1;
        if (migrated)
            Migrate(_settings);
        if (_settings.Schema != 2)
            throw new InvalidDataException("Unsupported settings schema.");
        if (_settings.Timing.Points.Count == 0)
            SeedTiming(_settings);
        Validate(_settings);
        if (migrated)
        {
            File.Copy(
                Path,
                Path + ".schema1-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak"
            );
            Update(_ => { });
        }
    }

    public AppSettings Read()
    {
        lock (_gate)
            return Clone(_settings);
    }

    public void Update(Action<AppSettings> edit)
    {
        lock (_gate)
        {
            var changed = Clone(_settings);
            edit(changed);
            Validate(changed);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(changed, Json));
            File.Move(temporary, Path, true);
            _settings = changed;
        }
    }

    public void EnsureDisplays(IEnumerable<DisplayDescriptor> displays)
    {
        var list = displays.ToArray();
        if (list.All(d => Read().Monitors.Any(m => m.Id == d.Id)))
            return;
        Update(settings =>
        {
            foreach (var display in list)
                if (settings.Monitors.All(m => m.Id != display.Id))
                {
                    var monitor = new MonitorProfile { Id = display.Id, Name = display.Name };
                    SeedColors(settings, monitor);
                    settings.Monitors.Add(monitor);
                }
        });
    }

    private static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;

    private static void Validate(AppSettings settings)
    {
        settings.Timing.Validate();
        if (settings.Monitors.Select(m => m.Id).Distinct().Count() != settings.Monitors.Count)
            throw new InvalidDataException("Duplicate monitor identities.");
        foreach (var monitor in settings.Monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.Id) || string.IsNullOrWhiteSpace(monitor.Name))
                throw new InvalidDataException("Invalid monitor identity.");
            monitor.Manual = monitor.Manual.Validate();
            SchedulePlan.ValidatePoints(monitor.OwnTiming);
            if (monitor.PointColors.Count > 96)
                throw new InvalidDataException("Too many point colors.");
            foreach (var id in monitor.PointColors.Keys.ToArray())
                monitor.PointColors[id] = monitor.PointColors[id].Validate();
            if (monitor.Schedule.Count > 0)
                DailySchedule.Evaluate(monitor.Schedule, TimeOnly.MinValue);
        }
        foreach (var preset in settings.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
                throw new InvalidDataException("A preset needs a name.");
            preset.Profile.Validate();
        }
    }

    internal static void SeedColors(AppSettings settings, MonitorProfile monitor)
    {
        var source = settings.Monitors.OrderByDescending(m => m.PointColors.Count).FirstOrDefault();
        foreach (var point in settings.Timing.Points)
            monitor.PointColors.TryAdd(
                point.Id,
                source is null ? DefaultColor(point.Time) : ScheduleTimeline.Color(source, point)
            );
    }

    private static ColorProfile DefaultColor(TimeOnly time) =>
        new(
            ColorMode.Temperature,
            time.Hour >= 22 || time.Hour < 7 ? 1800
                : time.Hour >= 10 && time.Hour < 17 ? 6600
                : 2700,
            1
        );

    private static void SeedTiming(AppSettings settings)
    {
        settings.Timing.Points = new[] { 9, 11, 17, 22 }
            .Select(hour => new TimingPoint("clock-" + hour, new(hour, 0)))
            .ToList();
    }

    private static void Migrate(AppSettings settings)
    {
        var source = settings
            .Monitors.Where(m => m.Schedule.Count > 0)
            .OrderByDescending(m => m.Scheduled)
            .ThenByDescending(m => m.Schedule.Count)
            .FirstOrDefault();
        if (source is null)
            SeedTiming(settings);
        else
        {
            settings.Timing.Points = source
                .Schedule.OrderBy(p => p.Time)
                .Select(p => new TimingPoint("clock-" + p.Time.ToString("HHmmss"), p.Time))
                .ToList();
            settings.Timing.TransitionMinutes = source
                .Schedule.GroupBy(p => p.TransitionMinutes)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
        }
        foreach (var monitor in settings.Monitors)
        {
            var old = monitor.Schedule;
            monitor.UseSharedSchedule =
                old.Count == 0
                || old.Select(p => p.Time)
                    .Order()
                    .SequenceEqual(settings.Timing.Points.Select(p => p.Time).Order());
            if (!monitor.UseSharedSchedule)
                monitor.OwnTiming = old.Select(p => new TimingPoint(
                        "clock-" + p.Time.ToString("HHmmss"),
                        p.Time
                    ))
                    .ToList();
            foreach (var point in ScheduleTimeline.Points(settings.Timing, monitor))
                monitor.PointColors[point.Id] =
                    old.FirstOrDefault(p => p.Time == point.Time)?.Profile
                    ?? source?.Schedule.FirstOrDefault(p => p.Time == point.Time)?.Profile
                    ?? DefaultColor(point.Time);
        }
        // The original file is backed up before persisting the new representation.
        foreach (var monitor in settings.Monitors)
            monitor.Schedule = [];
        settings.Schema = 2;
    }

    private static AppSettings Import(string? path)
    {
        var result = new AppSettings();
        if (path is null || !File.Exists(path))
            return result;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("DisplayProfiles", out var profiles))
            return result;
        foreach (var item in profiles.EnumerateArray())
        {
            var config = item.GetProperty("ManualConfiguration");
            var temperature = config.GetProperty("Temperature").GetDouble();
            var brightness = config.GetProperty("Brightness").GetDouble();
            result.Monitors.Add(
                new()
                {
                    Id = item.GetProperty("Id").GetString()!,
                    Name = item.GetProperty("Name").GetString()!,
                    Enabled = item.GetProperty("IsEnabled").GetBoolean(),
                    Manual = new(
                        temperature <= 1000 ? ColorMode.PureRed : ColorMode.Temperature,
                        temperature,
                        brightness
                    ),
                }
            );
        }
        if (document.RootElement.TryGetProperty("SelectedDisplayId", out var selected))
            result.SelectedMonitor = selected.GetString();
        return result;
    }
}
