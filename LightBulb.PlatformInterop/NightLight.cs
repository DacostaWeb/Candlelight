using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace LightBulb.PlatformInterop;

// Read-only detection of the known Windows 11 CloudStore schema. Unknown
// schemas are left alone; this class never changes Night Light preferences.
public static class NightLight
{
    private const string StatePath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    public static bool? ReadActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StatePath);
            return key?.GetValue("Data") is byte[] bytes ? DecodeActive(bytes) : null;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    internal static bool? DecodeActive(byte[] bytes)
    {
        try
        {
            if (bytes.Length > 1024)
                return null;
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Expect(reader, 0x43, 0x42, 1, 0, 0x0A, 0x02, 1, 0, 0x2A, 0x06);
            ReadVarint(reader);
            Expect(reader, 0x2A, 0x2B, 0x0E);
            var length = ReadVarint(reader);
            if (length < 5 || length > 1024 || stream.Position + (long)length + 3 != stream.Length)
                return null;
            var end = stream.Position + (long)length;
            Expect(reader, 0x43, 0x42, 1, 0);
            var active = false;
            var usable = true;
            var previous = -1;
            while (true)
            {
                var header = reader.ReadByte();
                if (header == 0)
                    break;
                int id;
                switch (header)
                {
                    case 0x10:
                        id = 0;
                        if (ReadVarint(reader) != 0)
                            return null;
                        active = true;
                        break;
                    case 0xD0:
                        Expect(reader, 10);
                        id = 10;
                        if (ReadVarint(reader) > 2)
                            return null;
                        break;
                    case 0xC6:
                        Expect(reader, 20);
                        id = 20;
                        ReadVarint(reader);
                        break;
                    case 0xC2:
                        Expect(reader, 30);
                        id = 30;
                        var flag = reader.ReadByte();
                        if (flag > 1)
                            return null;
                        usable = flag == 1;
                        break;
                    default:
                        return null;
                }
                if (id <= previous)
                    return null;
                previous = id;
            }
            if (stream.Position != end)
                return null;
            Expect(reader, 0, 0, 0);
            return active && usable;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static void Expect(BinaryReader reader, params byte[] bytes)
    {
        foreach (var expected in bytes)
            if (reader.ReadByte() != expected)
                throw new InvalidDataException("Unknown CloudStore schema.");
    }

    private static ulong ReadVarint(BinaryReader reader)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var next = reader.ReadByte();
            if (shift == 63 && next > 1)
                throw new InvalidDataException("Invalid CloudStore integer.");
            value |= (ulong)(next & 127) << shift;
            if (next < 128)
                return value;
        }
        throw new InvalidDataException("Invalid CloudStore integer.");
    }
}
