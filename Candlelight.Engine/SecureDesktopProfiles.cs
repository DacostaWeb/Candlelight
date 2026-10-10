using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Candlelight.Engine;

public sealed record SecureMonitorProfile(
    string Id,
    bool Enabled,
    ColorProfile Manual,
    SchedulePoint[] Schedule
)
{
    public ColorProfile Evaluate(TimeOnly time) =>
        Schedule.Length == 0 ? Manual : DailySchedule.Evaluate(Schedule, time);
}

/// <summary>Only bounded color data crosses into the secure desktop. No file paths or commands.</summary>
public static class SecureDesktopProfiles
{
    public const string Registration = "Candlelight_ColorFilter_v1";
    public const string SecureRegistration = "Candlelight_ColorFilterSecure_v1";
    public const string AccessibilityPath =
        @"Software\Microsoft\Windows NT\CurrentVersion\Accessibility";
    public const int MaximumBytes = 65536;

    private sealed record Payload(int Schema, SecureMonitorProfile[] Monitors);

    private static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] Encode(IEnumerable<SecureMonitorProfile> profiles)
    {
        var monitors = profiles.ToArray();
        Validate(monitors);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, monitors), Json);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("Secure profiles exceed their size limit.");
        return bytes;
    }

    public static SecureMonitorProfile[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("Invalid secure profile length.");
        var payload = JsonSerializer.Deserialize<Payload>(bytes, Json);
        if (payload is null || payload.Schema != 1)
            throw new InvalidDataException("Unsupported secure profile schema.");
        Validate(payload.Monitors);
        return payload.Monitors;
    }

    private static void Validate(SecureMonitorProfile[] monitors)
    {
        if (monitors is null || monitors.Length > 32)
            throw new InvalidDataException("Invalid monitor count.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var monitor in monitors)
        {
            if (
                monitor is null
                || string.IsNullOrWhiteSpace(monitor.Id)
                || monitor.Id.Length > 256
                || monitor.Id.Any(char.IsControl)
                || !ids.Add(monitor.Id)
            )
                throw new InvalidDataException("Invalid or duplicate monitor identity.");
            CheckColor(monitor.Manual);
            if (monitor.Schedule is null || monitor.Schedule.Length > 48)
                throw new InvalidDataException("Invalid schedule length.");
            foreach (var point in monitor.Schedule)
            {
                if (point is null || point.TransitionMinutes is < 0 or > 1440)
                    throw new InvalidDataException("Invalid schedule point.");
                CheckColor(point.Profile);
            }
            if (monitor.Schedule.Length != 0)
                DailySchedule.Evaluate(monitor.Schedule, TimeOnly.MinValue);
        }
    }

    private static void CheckColor(ColorProfile? profile)
    {
        if (
            profile is null
            || !Enum.IsDefined(profile.Mode)
            || !double.IsFinite(profile.Temperature)
            || profile.Temperature is < 500 or > 12000
            || !double.IsFinite(profile.Brightness)
            || profile.Brightness is < 0.01 or > 1
        )
            throw new InvalidDataException("Invalid secure color values.");
    }

    public static void Publish(byte[] bytes)
    {
        Decode(bytes);
        foreach (var registration in new[] { Registration, SecureRegistration })
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                AccessibilityPath + @"\ATConfig\" + registration
            );
            key.SetValue("Profiles", bytes, RegistryValueKind.Binary);
        }
    }

    public static SecureMonitorProfile[] Read()
    {
        var copied = ReadHive(Registry.CurrentUser, "");
        if (copied is not null)
            return copied;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (identity.IsSystem)
        {
            // Some Windows builds launch the alternate AT without copying its
            // configuration. Identify this session's owner through Windows,
            // then read only our bounded, untrusted color data from their hive.
            // No impersonation, user token acquisition or user-supplied paths.
            var sid = SessionAccount.UserSid(
                System.Diagnostics.Process.GetCurrentProcess().SessionId
            );
            var sessionProfiles = ReadHive(Registry.Users, sid + "\\");
            if (sessionProfiles is not null)
                return sessionProfiles;
        }
        throw new InvalidDataException("Secure profile transfer is unavailable.");
    }

    private static SecureMonitorProfile[]? ReadHive(RegistryKey hive, string prefix)
    {
        foreach (var registration in new[] { SecureRegistration, Registration })
        {
            using var key = hive.OpenSubKey(
                prefix + AccessibilityPath + @"\ATConfig\" + registration
            );
            if (key is null)
                continue;
            uint length = 0;
            var result = RegQueryValueEx(key.Handle, "Profiles", 0, out var type, null, ref length);
            if (result == 2) // ERROR_FILE_NOT_FOUND: try the other documented AT key.
                continue;
            if (result != 0 || type != 3 || length is < 1 or > MaximumBytes)
                throw new InvalidDataException("Secure profile transfer is invalid.");
            var bytes = new byte[length];
            result = RegQueryValueEx(key.Handle, "Profiles", 0, out type, bytes, ref length);
            if (result != 0 || type != 3 || length != bytes.Length)
                throw new InvalidDataException("Secure profile transfer changed while reading.");
            return Decode(bytes);
        }
        return null;
    }

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(
        SafeRegistryHandle key,
        string name,
        nint reserved,
        out uint type,
        byte[]? data,
        ref uint length
    );
}
