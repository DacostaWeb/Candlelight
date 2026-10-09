using System.Text.Json;
using System.Text.Json.Serialization;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed record Preset(string Name, ColorProfile Profile);

internal sealed record AppSettings
{
    public int Schema { get; init; } = 1;
    public bool FilterCursor { get; set; } = true;
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
        if (_settings.Schema != 1)
            throw new InvalidDataException("Unsupported settings schema.");
        Validate(_settings);
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
                    settings.Monitors.Add(new() { Id = display.Id, Name = display.Name });
        });
    }

    private static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;

    private static void Validate(AppSettings settings)
    {
        if (settings.Monitors.Select(m => m.Id).Distinct().Count() != settings.Monitors.Count)
            throw new InvalidDataException("Duplicate monitor identities.");
        foreach (var monitor in settings.Monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.Id) || string.IsNullOrWhiteSpace(monitor.Name))
                throw new InvalidDataException("Invalid monitor identity.");
            monitor.Manual = monitor.Manual.Validate();
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
