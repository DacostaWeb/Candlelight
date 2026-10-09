using System.Runtime.InteropServices;

namespace Candlelight.Engine;

internal static class Native
{
    internal const uint CommandMessage = 0x8001;
    internal delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);
    internal delegate bool MonitorEnum(nint monitor, nint dc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X,
            Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Bounds,
            WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size,
            Style;
        public WndProc Procedure;
        public int ClassExtra,
            WindowExtra;
        public nint Instance,
            Icon,
            Cursor,
            Background;
        public string? Menu;
        public string Name;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X,
            Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Transform
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] Values;
        public static Transform Identity => new() { Values = [1, 0, 0, 0, 1, 0, 0, 0, 1] };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] Values;

        public static ColorEffect FromGain(ChannelGain gain) =>
            new()
            {
                Values =
                [
                    (float)gain.Red,
                    0,
                    0,
                    0,
                    0,
                    0,
                    (float)gain.Green,
                    0,
                    0,
                    0,
                    0,
                    0,
                    (float)gain.Blue,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                ],
            };
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagShowSystemCursor([MarshalAs(UnmanagedType.Bool)] bool show);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagSetWindowTransform(nint window, ref Transform transform);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagSetColorEffect(nint window, ref ColorEffect effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagGetColorEffect(nint window, out ColorEffect effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagSetFullscreenColorEffect(ref ColorEffect effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagGetFullscreenColorEffect(out ColorEffect effect);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagSetWindowSource(nint window, Rect source);

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MagSetWindowFilterList(
        nint window,
        uint mode,
        int count,
        nint[] windows
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClass value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string? title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint data
    );

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(
        nint window,
        nint rectangle,
        [MarshalAs(UnmanagedType.Bool)] bool erase
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(
        nint window,
        uint color,
        byte alpha,
        uint flags
    );

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool KillTimer(nint window, nuint id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint dc,
        nint clip,
        MonitorEnum callback,
        nint data
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string deviceName,
        uint number,
        ref DisplayDevice device,
        uint flags
    );

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint RegisterPowerSettingNotification(
        nint recipient,
        ref Guid setting,
        uint flags
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterPowerSettingNotification(nint handle);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? name);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();
}
