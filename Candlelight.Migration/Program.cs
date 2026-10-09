using System.Text.Json;
using LightBulb.Core;
using LightBulb.PlatformInterop;
using LightBulb.PlatformInterop.Internal;

// One-shot cleanup of known Candlelight 0.1.x filters, kept out of the new app.
// Unknown ramps are left untouched. Run only after the new renderer is active.
if (
    args.Length is < 1 or > 2
    || !File.Exists(args[0])
    || args.Length == 2 && args[1] != "--night-light-off"
)
    return 1;
if (System.Diagnostics.Process.GetProcessesByName("Candlelight").Length != 0)
    throw new InvalidOperationException("Close the legacy Candlelight before migration.");
var isolateRenderer = args.Length == 2;
if (isolateRenderer)
{
    var active = NightLight.ReadActive();
    if (active is null)
        return 2;
    File.WriteAllText(
        Path.Combine(AppContext.BaseDirectory, "NightLightBeforeNext.txt"),
        active.Value ? "on" : "off"
    );
    if (!NightLight.TrySetActive(false))
        return 2;
    Thread.Sleep(1500);
    Console.WriteLine(
        "Night Light disabled for the isolated renderer test; prior active state saved."
    );
}
using var settings = JsonDocument.Parse(File.ReadAllText(args[0]));
var profiles = settings
    .RootElement.GetProperty("DisplayProfiles")
    .EnumerateArray()
    .ToDictionary(p => p.GetProperty("Id").GetString()!);
var success = true;
foreach (var monitor in LightBulb.PlatformInterop.Monitor.GetAll())
{
    using (monitor)
    {
        if (
            monitor.TryGetDisplayInfo() is not { } display
            || !profiles.TryGetValue(display.Id, out var profile)
        )
            continue;
        using var dc = monitor.TryCreateDeviceContext();
        if (dc is null)
        {
            success = false;
            continue;
        }
        var configuration = profile.GetProperty("ManualConfiguration");
        var gain = GammaColor.FromConfiguration(
            new(
                configuration.GetProperty("Temperature").GetDouble(),
                configuration.GetProperty("Brightness").GetDouble()
            )
        );
        var expected = GammaRamp.Create(gain.Red, gain.Green, gain.Blue, 0);
        var identity = GammaRamp.Identity();
        var api = new NativeGammaRampApi(dc.Handle);
        if (api.ReadColorSystem(out var color) && !identity.Matches(color))
        {
            if (!expected.Matches(color))
            {
                Console.WriteLine(display.Name + ": unknown color-system ramp; not changed.");
                success = false;
            }
            else if (!api.WriteColorSystem(ref identity))
                success = false;
            else
                Console.WriteLine(display.Name + ": known legacy color-system filter cleared.");
        }
        if (api.ReadGdi(out var gdi) && !identity.Matches(gdi))
        {
            if (!expected.Matches(gdi))
            {
                Console.WriteLine(display.Name + ": unknown GDI ramp; not changed.");
                success = false;
            }
            else if (!api.WriteGdi(ref identity))
                success = false;
            else
                Console.WriteLine(display.Name + ": known legacy GDI filter cleared.");
        }
        Console.WriteLine(display.Name + ": " + dc.GetDiagnostics());
    }
}
var token = Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(args[0]))!,
    "NightLightGuard.state"
);
if (File.Exists(token))
{
    var saved = File.ReadAllText(token).Trim();
    if (saved is "on" or "off" && (isolateRenderer || NightLight.TrySetActive(saved == "on")))
    {
        File.Move(token, token + ".migrated", true);
        Console.WriteLine("Restored original Night Light state: " + saved);
    }
    else
    {
        Console.WriteLine("Night Light ownership could not be restored.");
        success = false;
    }
}
Console.WriteLine("Night Light active: " + NightLight.ReadActive());
return success ? 0 : 2;
