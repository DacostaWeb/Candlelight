using System.Runtime.InteropServices;

namespace LightBulb.PlatformInterop.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct GammaRamp
{
    internal static GammaRamp Create(double red, double green, double blue, int refreshOffset)
    {
        var ramp = new GammaRamp
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256],
        };
        for (var i = 0; i < 256; i++)
        {
            ramp.Red[i] = (ushort)(i * 255 * red);
            ramp.Green[i] = (ushort)(i * 255 * green);
            ramp.Blue[i] = (ushort)(i * 255 * blue);
        }
        if (ramp.Red[255] != 0)
            ramp.Red[255] = (ushort)(ramp.Red[255] + refreshOffset);
        if (ramp.Green[255] != 0)
            ramp.Green[255] = (ushort)(ramp.Green[255] + refreshOffset);
        if (ramp.Blue[255] != 0)
            ramp.Blue[255] = (ushort)(ramp.Blue[255] + refreshOffset);
        return ramp;
    }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Red { get; init; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Green { get; init; }

    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Blue { get; init; }
}
