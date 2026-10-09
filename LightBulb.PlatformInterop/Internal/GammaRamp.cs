using System.Runtime.InteropServices;

namespace LightBulb.PlatformInterop.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct GammaRamp
{
    internal static GammaRamp Identity()
    {
        var ramp = new GammaRamp
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256],
        };
        for (var i = 0; i < 256; i++)
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)(i * 257);
        return ramp;
    }

    internal bool Matches(GammaRamp other) =>
        ChannelMatches(Red, other.Red)
        && ChannelMatches(Green, other.Green)
        && ChannelMatches(Blue, other.Blue);

    private static bool ChannelMatches(ushort[]? expected, ushort[]? actual)
    {
        if (expected is not { Length: 256 } || actual is not { Length: 256 })
            return false;
        var isZero = System.Array.TrueForAll(expected, value => value == 0);
        for (var i = 0; i < 256; i++)
        {
            // Permit one 8-bit LUT step of driver quantization, but never accept
            // a nonzero channel in red-only mode.
            if (isZero ? actual[i] != 0 : System.Math.Abs(expected[i] - actual[i]) > 257)
                return false;
        }
        return true;
    }

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
