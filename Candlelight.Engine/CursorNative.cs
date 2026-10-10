using System.Runtime.InteropServices;

namespace Candlelight.Engine;

internal static class CursorNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorInfo
    {
        public int Size;
        public uint Flags;
        public nint Cursor;
        public Native.Point Position;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref CursorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX,
            HotspotY;
        public nint Mask,
            Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap
    {
        public int Type,
            Width,
            Height,
            WidthBytes;
        public ushort Planes,
            BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width,
            Height;
        public ushort Planes,
            Bits;
        public uint Compression,
            ImageSize;
        public int XPels,
            YPels;
        public uint ColorsUsed,
            ImportantColors;
        public uint Black,
            White;

        public static BitmapInfo For(int width, int height, ushort bits) =>
            new()
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                Bits = bits,
                White = 0x00FFFFFF,
            };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint LoadCursor(nint instance, nint resource);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(nint icon, out IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemCursor(nint cursor, uint id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyCursor(nint cursor);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    internal static extern int GetObject(nint bitmap, int size, out Bitmap info);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDIBits(
        nint dc,
        nint bitmap,
        uint start,
        uint lines,
        byte[] data,
        ref BitmapInfo info,
        uint usage
    );

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(
        nint dc,
        ref BitmapInfo info,
        uint usage,
        out nint bits,
        nint section,
        uint offset
    );

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint value);
}
