using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LightBulb.PlatformInterop.Internal;

namespace LightBulb.PlatformInterop;

public partial class DeviceContext(nint handle, string? deviceName = null)
    : NativeResource(handle),
        IGammaDevice
{
    private static readonly ConcurrentDictionary<string, bool> ColorSystemDevices = new();
    private int _gammaChannelOffset;
    private readonly NativeGammaRampApi _api = new(handle);
    private GammaRampWriter? _writer;

    private GammaRampWriter Writer =>
        _writer ??= new(_api, deviceName is not null && ColorSystemDevices.ContainsKey(deviceName));
    public string? FailureReason => Writer.FailureReason;
    public string? ApplyDiagnostics => Writer.LastOperation;
    public bool UsesColorSystem => Writer.UsesColorSystem;

    private bool SetGammaRamp(GammaRamp ramp)
    {
        var applied = Writer.Apply(ramp);
        // Contexts are recreated after wake and display changes. Preserve backend
        // ownership so a later daytime value cannot leave an old red LUT active.
        if (Writer.UsesColorSystem && deviceName is not null)
            ColorSystemDevices[deviceName] = true;
        if (!applied)
        {
            Debug.WriteLine(
                $"Failed to set gamma ramp on device context #{Handle}). "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
            return false;
        }

        return true;
    }

    public bool SetGamma(double redMultiplier, double greenMultiplier, double blueMultiplier)
    {
        // Some drivers will ignore requests to change gamma if the specified ramp is the same as last time,
        // even if the actual gamma has been changed in-between (for example, by screen going to sleep).
        // In order to work around this, we add a small random deviation to each ramp to make sure
        // they're always unique, forcing the drivers to refresh the device context every time.
        _gammaChannelOffset = ++_gammaChannelOffset % 5;
        return SetGammaRamp(
            GammaRamp.Create(redMultiplier, greenMultiplier, blueMultiplier, _gammaChannelOffset)
        );
    }

    public void ResetGamma()
    {
        Writer.Reset();
        if (deviceName is not null)
            ColorSystemDevices.TryRemove(deviceName, out _);
    }

    public string GetDiagnostics()
    {
        var gdi = _api.ReadGdi(out var gdiRamp)
            ? $"R={gdiRamp.Red[255]}, G={gdiRamp.Green[255]}, B={gdiRamp.Blue[255]}"
            : "indisponível";
        var color = _api.ReadColorSystem(out var colorRamp)
            ? $"R={colorRamp.Red[255]}, G={colorRamp.Green[255]}, B={colorRamp.Blue[255]}"
            : "indisponível";
        return $"GDI: {gdi}; sistema de cor: {color}; compatibilidade disponível: {_api.IsColorSystemAvailable}";
    }

    protected override void Dispose(bool disposing)
    {
        // Don't reset gamma during dispose because this method is also called whenever
        // the device context gets invalidated.
        // Resetting gamma in such cases will cause unwanted flickering.
        // https://github.com/Tyrrrz/LightBulb/issues/206
        if (!NativeMethods.DeleteDC(Handle))
        {
            Debug.WriteLine(
                $"Failed to dispose device context #{Handle}. "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
        }
    }
}

public partial class DeviceContext
{
    public static DeviceContext? TryCreate(string deviceName)
    {
        var handle = NativeMethods.CreateDC(deviceName, deviceName, null, 0);
        if (handle == 0)
        {
            Debug.WriteLine(
                $"Failed to retrieve device context for '{deviceName}'. "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );
            return null;
        }

        return new DeviceContext(handle, deviceName);
    }
}
