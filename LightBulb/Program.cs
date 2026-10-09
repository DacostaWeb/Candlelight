using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Avalonia;
using LightBulb.PlatformInterop;

namespace LightBulb;

public static class Program
{
    private static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();

    public static string Name { get; } = Assembly.GetName().Name ?? "LightBulb";

    public static Version Version { get; } = Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string VersionString { get; } = Version.ToString(3);

    public static bool IsDevelopmentBuild { get; } = Version.Major >= 999;

    public static string ExecutableDirPath { get; } = AppContext.BaseDirectory;

    public static string ExecutableFilePath { get; } =
        Path.ChangeExtension(Assembly.Location, "exe");

    public static string ProjectUrl { get; } = "https://github.com/DacostaWeb/Candlelight";

    public static string ProjectReleasesUrl { get; } = $"{ProjectUrl}/releases";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(
                new Win32PlatformOptions
                {
                    // Use redirection surface composition to avoid Avalonia's WinUI Composition
                    // renderer (WinUiCompositorConnection) from ticking endlessly via dcomp.dll
                    // when the application is idle, which would otherwise wake up dwm.exe
                    // continuously even when monitors are powered off.
                    CompositionMode = [Win32CompositionMode.RedirectionSurface],
                    RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
                }
            )
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        if (
            Array.Exists(args, a => a.Equals("--list-displays", StringComparison.OrdinalIgnoreCase))
        )
        {
            foreach (var monitor in LightBulb.PlatformInterop.Monitor.GetAll())
            {
                using (monitor)
                {
                    if (monitor.TryGetDisplayInfo() is { } info)
                        Console.WriteLine(
                            $"{info.Name} | {info.DeviceName} | {info.Bounds.Right - info.Bounds.Left}x{info.Bounds.Bottom - info.Bounds.Top} | {(info.IsPrimary ? "primary" : "secondary")} | physical identity: {info.Id.StartsWith("edid:")}"
                        );
                }
            }
            return 0;
        }
        // Ensure only one instance of the app is running at a time
        using var identityMutex = new Mutex(
            true,
            $"{Name}_Identity{(StartOptions.Current.IsPreview ? "_Preview" : "")}",
            out var isOnlyRunningInstance
        );

        if (!isOnlyRunningInstance)
            return 1;

        // Build and run the app
        var builder = BuildAvaloniaApp();

        try
        {
            return builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            MessageBox.ShowError("Fatal Error", ex.ToString());
            throw;
        }
        finally
        {
            // Clean up after application shutdown
            if (builder.Instance is IDisposable disposableApp)
                disposableApp.Dispose();
        }
    }
}
