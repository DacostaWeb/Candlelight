using System.Runtime.InteropServices;
using Candlelight.Engine;
using Microsoft.Win32;

namespace Candlelight.Next;

internal static class AssistiveTechnology
{
    internal static void Notify(bool running)
    {
        using var registration = Registry.LocalMachine.OpenSubKey(
            SecureDesktopProfiles.AccessibilityPath + @"\ATs\" + SecureDesktopProfiles.Registration
        );
        if (
            registration?.GetValue("StartExe") is not string installed
            || !string.Equals(
                installed,
                Environment.ProcessPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return;
        using var settings = Registry.CurrentUser.CreateSubKey(
            SecureDesktopProfiles.AccessibilityPath
        );
        var active = ((settings.GetValue("Configuration") as string) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v != SecureDesktopProfiles.Registration)
            .ToList();
        if (running)
            active.Add(SecureDesktopProfiles.Registration);
        settings.SetValue("Configuration", string.Join(',', active), RegistryValueKind.String);
        using var temporary = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows NT\CurrentVersion\AccessibilityTemp"
        );
        temporary.SetValue(
            SecureDesktopProfiles.Registration,
            running ? 3 : 2,
            RegistryValueKind.DWord
        );
        // Documented AT start/stop notification. This is never used on Winlogon.
        Input[] input = [Key(0x5B), Key(0x55), Key(0x55, true), Key(0x5B, true)];
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length)
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Accessibility notification failed."
            );
    }

    private static Input Key(ushort key, bool up = false) =>
        new()
        {
            Type = 1,
            Data = new() { Key = key, Flags = up ? 2u : 0u },
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Data;
    }

    // INPUT's union has the size/alignment of MOUSEINPUT on x64 (32 bytes).
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct KeyboardInput
    {
        [FieldOffset(0)]
        public ushort Key;

        [FieldOffset(4)]
        public uint Flags;

        [FieldOffset(16)]
        public nuint Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] input, int size);
}
