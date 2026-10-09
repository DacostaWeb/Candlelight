using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Candlelight.Engine;

public sealed record DisplayDescriptor(
    string Id,
    string Name,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool Primary
);

public static class DisplayCatalog
{
    public static IReadOnlyList<DisplayDescriptor> GetDisplays()
    {
        var result = new List<DisplayDescriptor>();
        Native.MonitorEnum callback = (monitor, _, _, _) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info))
                return true;
            var device = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>() };
            var found = Native.EnumDisplayDevices(info.DeviceName, 0, ref device, 1);
            var connection =
                found && !string.IsNullOrWhiteSpace(device.DeviceId)
                    ? device.DeviceId
                    : info.DeviceName;
            byte[]? edid = null;
            try
            {
                var parts = connection.Split('#');
                if (parts.Length >= 3)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters"
                    );
                    edid = key?.GetValue("EDID") as byte[];
                }
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or System.Security.SecurityException
                ) { }
            result.Add(
                new(
                    MonitorIdentity.GetId(edid, connection),
                    MonitorIdentity.GetName(edid)
                        ?? (found ? device.DeviceString : info.DeviceName),
                    info.DeviceName,
                    info.Bounds.Left,
                    info.Bounds.Top,
                    info.Bounds.Right - info.Bounds.Left,
                    info.Bounds.Bottom - info.Bounds.Top,
                    (info.Flags & 1) != 0
                )
            );
            return true;
        };
        if (!Native.EnumDisplayMonitors(0, 0, callback, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
}
