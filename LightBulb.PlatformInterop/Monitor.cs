using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using LightBulb.PlatformInterop.Internal;
using Microsoft.Win32;

namespace LightBulb.PlatformInterop;

public partial class Monitor(nint handle) : NativeResource(handle)
{
    private MonitorInfoEx? TryGetMonitorInfo()
    {
        var monitorInfo = new MonitorInfoEx();

        if (!NativeMethods.GetMonitorInfo(Handle, ref monitorInfo))
        {
            Debug.WriteLine(
                $"Failed to retrieve info for monitor #{Handle}. "
                    + $"Error {Marshal.GetLastWin32Error()}."
            );

            return null;
        }

        return monitorInfo;
    }

    public Rect? TryGetBounds() => TryGetMonitorInfo()?.Monitor;

    public string? TryGetDeviceName() => TryGetMonitorInfo()?.DeviceName;

    public DisplayInfo? TryGetDisplayInfo()
    {
        if (TryGetMonitorInfo() is not { DeviceName: { } name } info)
            return null;

        var device = new DisplayDevice();
        // EDD_GET_DEVICE_INTERFACE_NAME returns the monitor's persistent interface path.
        var found = NativeMethods.EnumDisplayDevices(name, 0, ref device, 1);
        var connectionId =
            found && !string.IsNullOrWhiteSpace(device.DeviceId) ? device.DeviceId : name;
        var edid = TryReadEdid(connectionId);
        return new DisplayInfo(
            MonitorIdentity.GetId(edid, connectionId),
            name,
            MonitorIdentity.GetName(edid) is { Length: > 0 } model ? model
                : found && !string.IsNullOrWhiteSpace(device.DeviceString) ? device.DeviceString
                : name,
            (info.Flags & 1) != 0,
            info.Monitor
        )
        {
            ConnectionId = connectionId,
        };
    }

    private static byte[]? TryReadEdid(string interfacePath)
    {
        var parts = interfacePath.Split('#');
        if (parts.Length < 3)
            return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters"
            );
            return key?.GetValue("EDID") as byte[];
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    public DeviceContext? TryCreateDeviceContext()
    {
        var name = TryGetDeviceName();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return DeviceContext.TryCreate(name);
    }

    protected override void Dispose(bool disposing) { }
}

public partial class Monitor
{
    public static IReadOnlyList<Monitor> GetAll()
    {
        var monitors = new List<Monitor>();

        if (
            !NativeMethods.EnumDisplayMonitors(
                0,
                0,
                (hMonitor, _, _, _) =>
                {
                    monitors.Add(new Monitor(hMonitor));
                    return true;
                },
                0
            )
        )
        {
            Debug.WriteLine(
                "Failed to enumerate display monitors. " + $"Error {Marshal.GetLastWin32Error()}."
            );
        }

        return monitors;
    }
}
