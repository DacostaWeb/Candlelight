using System;
using System.Linq;
using System.Text;

namespace LightBulb.PlatformInterop;

public static class MonitorIdentity
{
    public static string GetId(byte[]? edid, string connectionId)
    {
        if (!IsValid(edid))
            return connectionId;
        var serialText = GetDescriptor(edid!, 0xFF);
        var numericSerial = BitConverter.ToUInt32(edid!, 12);
        var serial =
            !string.IsNullOrWhiteSpace(serialText)
            && serialText.Any(c => c != '0' && !char.IsWhiteSpace(c))
                ? serialText.ToUpperInvariant()
            : numericSerial is not (0 or uint.MaxValue) ? numericSerial.ToString("X8")
            : null;
        if (serial is null)
            return connectionId;
        return $"edid:{edid![8]:X2}{edid[9]:X2}:{BitConverter.ToUInt16(edid, 10):X4}:{serial}";
    }

    public static string? GetName(byte[]? edid) =>
        IsValid(edid) ? GetDescriptor(edid!, 0xFC) : null;

    private static bool IsValid(byte[]? edid) =>
        edid is { Length: >= 128 }
        && edid.Take(8).SequenceEqual(new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 })
        && edid.Take(128).Sum(b => (int)b) % 256 == 0;

    private static string? GetDescriptor(byte[] edid, byte tag)
    {
        for (var offset = 54; offset <= 108; offset += 18)
            if (
                edid[offset] == 0
                && edid[offset + 1] == 0
                && edid[offset + 2] == 0
                && edid[offset + 3] == tag
            )
                return Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
        return null;
    }
}
