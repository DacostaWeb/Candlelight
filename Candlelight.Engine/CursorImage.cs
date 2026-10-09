using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Candlelight.Engine;

/// <summary>Cursor pixels only: never samples the desktop behind a pointer.</summary>
internal sealed record CursorImage(
    int Width,
    int Height,
    uint HotspotX,
    uint HotspotY,
    byte[] Pixels,
    byte[] Mask
)
{
    internal static CursorImage Read(nint cursor)
    {
        if (!CursorNative.GetIconInfo(cursor, out var icon))
            throw MagnificationEngine.Error("Read cursor image");
        var dc = CursorNative.CreateCompatibleDC(0);
        try
        {
            if (
                dc == 0
                || CursorNative.GetObject(
                    icon.Mask,
                    Marshal.SizeOf<CursorNative.Bitmap>(),
                    out var maskInfo
                ) == 0
            )
                throw MagnificationEngine.Error("Read cursor dimensions");
            var width = maskInfo.Width;
            var height = icon.Color != 0 ? maskInfo.Height : maskInfo.Height / 2;
            if (width is <= 0 or > 1024 || height is <= 0 or > 1024)
                throw new InvalidDataException("Invalid cursor dimensions.");
            var stride = ((width + 31) / 32) * 4;
            var mask = ReadBits(dc, icon.Mask, width, maskInfo.Height, 1);
            byte[] pixels;
            if (icon.Color != 0)
            {
                pixels = ReadBits(dc, icon.Color, width, height, 32);
                if (!Enumerable.Range(0, width * height).Any(i => pixels[i * 4 + 3] != 0))
                    for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        pixels[(y * width + x) * 4 + 3] = Bit(mask, stride, x, y)
                            ? (byte)0
                            : (byte)255;
            }
            else
            {
                pixels = Monochrome(width, height, mask);
            }
            return new(
                width,
                height,
                icon.HotspotX,
                icon.HotspotY,
                pixels,
                mask[..(stride * height)]
            );
        }
        finally
        {
            if (dc != 0)
                CursorNative.DeleteDC(dc);
            if (icon.Mask != 0)
                CursorNative.DeleteObject(icon.Mask);
            if (icon.Color != 0)
                CursorNative.DeleteObject(icon.Color);
        }
    }

    private static byte[] ReadBits(nint dc, nint bitmap, int width, int height, ushort bits)
    {
        var info = CursorNative.BitmapInfo.For(width, height, bits);
        var bytes = new byte[checked(((width * bits + 31) / 32) * 4 * height)];
        if (CursorNative.GetDIBits(dc, bitmap, 0, (uint)height, bytes, ref info, 0) != height)
            throw MagnificationEngine.Error("Read cursor pixels");
        return bytes;
    }

    // XOR cursors cannot retain background inversion once colored. Render their
    // visible strokes in white with a black edge, retaining size and hotspot.
    internal static byte[] Monochrome(int width, int height, byte[] mask)
    {
        var stride = ((width + 31) / 32) * 4;
        if (mask.Length != stride * height * 2)
            throw new ArgumentException("Invalid monochrome mask.");
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var and = Bit(mask, stride, x, y);
            var xor = Bit(mask, stride, x, y + height);
            var offset = (y * width + x) * 4;
            if (!and || xor)
            {
                pixels[offset] =
                    pixels[offset + 1] =
                    pixels[offset + 2] =
                        xor ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
            else if (
                Enumerable
                    .Range(-1, 3)
                    .Any(dy =>
                        Enumerable
                            .Range(-1, 3)
                            .Any(dx =>
                                x + dx >= 0
                                && x + dx < width
                                && y + dy >= 0
                                && y + dy < height
                                && Bit(mask, stride, x + dx, y + dy + height)
                            )
                    )
            )
                pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static bool Bit(byte[] mask, int stride, int x, int y) =>
        (mask[y * stride + x / 8] & (0x80 >> (x % 8))) != 0;

    internal byte[] Tint(ChannelGain gain)
    {
        var result = Pixels.ToArray();
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = (byte)Math.Round(result[i] * gain.Blue);
            result[i + 1] = (byte)Math.Round(result[i + 1] * gain.Green);
            result[i + 2] = (byte)Math.Round(result[i + 2] * gain.Red);
        }
        return result;
    }

    internal string Fingerprint =>
        Convert.ToHexString(
            SHA256.HashData(
                BitConverter
                    .GetBytes(Width)
                    .Concat(BitConverter.GetBytes(Height))
                    .Concat(BitConverter.GetBytes(HotspotX))
                    .Concat(BitConverter.GetBytes(HotspotY))
                    .Concat(Pixels)
                    .Concat(Mask)
                    .ToArray()
            )
        );

    internal nint Create(ChannelGain gain)
    {
        if (
            Width is <= 0 or > 1024
            || Height is <= 0 or > 1024
            || Pixels.Length != Width * Height * 4
            || Mask.Length != ((Width + 31) / 32) * 4 * Height
            || HotspotX >= Width
            || HotspotY >= Height
        )
            throw new InvalidDataException("Invalid cursor image.");
        var colorInfo = CursorNative.BitmapInfo.For(Width, Height, 32);
        var maskInfo = CursorNative.BitmapInfo.For(Width, Height, 1);
        var color = CursorNative.CreateDIBSection(0, ref colorInfo, 0, out var colorBits, 0, 0);
        var mask = CursorNative.CreateDIBSection(0, ref maskInfo, 0, out var maskBits, 0, 0);
        try
        {
            if (color == 0 || mask == 0)
                throw MagnificationEngine.Error("Create colored cursor bitmap");
            var pixels = Tint(gain);
            Marshal.Copy(pixels, 0, colorBits, pixels.Length);
            Marshal.Copy(Mask, 0, maskBits, Mask.Length);
            var icon = new CursorNative.IconInfo
            {
                HotspotX = HotspotX,
                HotspotY = HotspotY,
                Mask = mask,
                Color = color,
            };
            var result = CursorNative.CreateIconIndirect(ref icon);
            if (result == 0)
                throw MagnificationEngine.Error("Create colored cursor");
            return result;
        }
        finally
        {
            if (color != 0)
                CursorNative.DeleteObject(color);
            if (mask != 0)
                CursorNative.DeleteObject(mask);
        }
    }
}
