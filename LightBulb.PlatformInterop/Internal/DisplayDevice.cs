using System.Runtime.InteropServices;

namespace LightBulb.PlatformInterop.Internal;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayDevice
{
    public int Size;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName = "";

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString = "";
    public uint StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceId = "";

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey = "";

    public DisplayDevice() => Size = Marshal.SizeOf<DisplayDevice>();
}
