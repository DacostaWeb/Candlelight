using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LightBulb.PlatformInterop.Internal;

internal sealed class NativeGammaRampApi(nint handle) : IGammaRampApi
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate bool WriteColor(nint context, ref GammaRamp ramp, uint reserved);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate bool ReadColor(nint context, out GammaRamp ramp);

    private static readonly WriteColor? ColorWriter;
    private static readonly ReadColor? ColorReader;

    static NativeGammaRampApi()
    {
        // These exports are internal Windows APIs. Resolve both at runtime,
        // exclusively from the system DLL; unsupported systems keep the GDI path.
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "mscms.dll"
        );
        if (!NativeLibrary.TryLoad(path, out var library))
            return;
        if (
            NativeLibrary.TryGetExport(library, "InternalSetDeviceGammaRamp", out var write)
            && NativeLibrary.TryGetExport(library, "InternalGetAppliedGammaRamp", out var read)
        )
        {
            ColorWriter = Marshal.GetDelegateForFunctionPointer<WriteColor>(write);
            ColorReader = Marshal.GetDelegateForFunctionPointer<ReadColor>(read);
            // Keep the module loaded for the process lifetime while delegates exist.
        }
        else
            NativeLibrary.Free(library);
    }

    public bool IsColorSystemAvailable => ColorWriter is not null && ColorReader is not null;

    public bool WriteGdi(ref GammaRamp ramp) => NativeMethods.SetDeviceGammaRamp(handle, ref ramp);

    public bool ReadGdi(out GammaRamp ramp) => NativeMethods.GetDeviceGammaRamp(handle, out ramp);

    public bool WriteColorSystem(ref GammaRamp ramp) =>
        ColorWriter?.Invoke(handle, ref ramp, 0) ?? false;

    public bool ReadColorSystem(out GammaRamp ramp)
    {
        ramp = default;
        return ColorReader?.Invoke(handle, out ramp) ?? false;
    }
}
