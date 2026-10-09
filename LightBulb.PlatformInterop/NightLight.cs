using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace LightBulb.PlatformInterop;

// Strict support for the known Windows 11 CloudStore state schema. Unknown
// schemas are left alone. Strength and schedule settings are never written.
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

    private sealed record State(
        ulong Modified,
        bool Active,
        bool Usable,
        SortedDictionary<int, byte[]> Fields
    );

    internal static bool? DecodeActive(byte[] bytes)
    {
        var state = DecodeState(bytes);
        return state is null ? null : state.Active && state.Usable;
    }

    // Returning false never means success: callers keep their protection in
    // place if Windows rejects the change or exposes an unsupported schema.
    public static bool TrySetActive(bool active)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StatePath, true);
            if (key?.GetValue("Data") is not byte[] before)
                return false;
            var updated = RewriteActive(before, active, DateTimeOffset.UtcNow);
            if (updated is null)
                return false;
            if (before.SequenceEqual(updated))
                return true;
            // Avoid overwriting a concurrent Windows scheduler change.
            if (key.GetValue("Data") is not byte[] latest || !before.SequenceEqual(latest))
                return false;
            key.SetValue("Data", updated, RegistryValueKind.Binary);
            return key.GetValue("Data") is byte[] actual && DecodeActive(actual) == active;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    internal static byte[]? RewriteActive(byte[] bytes, bool active, DateTimeOffset now)
    {
        var state = DecodeState(bytes);
        if (
            state is null
            || !state.Usable
            || state.Modified > ulong.MaxValue - 2
            || now < DateTimeOffset.UnixEpoch
        )
            return null;
        if (state.Active == active)
            return bytes;
        if (active)
            state.Fields[0] = [0x10, 0];
        else
            state.Fields.Remove(0);
        state.Fields[10] = [0xD0, 10, 2]; // Manual transition, ZigZag(1).
        using var timeField = new MemoryStream();
        using (var writer = new BinaryWriter(timeField, System.Text.Encoding.UTF8, true))
        {
            writer.Write(new byte[] { 0xC6, 20 });
            WriteVarint(writer, (ulong)now.UtcDateTime.ToFileTimeUtc());
        }
        state.Fields[20] = timeField.ToArray();
        using var inner = new MemoryStream();
        using (var writer = new BinaryWriter(inner, System.Text.Encoding.UTF8, true))
        {
            writer.Write(new byte[] { 0x43, 0x42, 1, 0 });
            foreach (var field in state.Fields.Values)
                writer.Write(field);
            writer.Write((byte)0);
        }
        using var outer = new MemoryStream();
        using (var writer = new BinaryWriter(outer, System.Text.Encoding.UTF8, true))
        {
            writer.Write(new byte[] { 0x43, 0x42, 1, 0, 0x0A, 0x02, 1, 0, 0x2A, 0x06 });
            WriteVarint(writer, Math.Max((ulong)now.ToUnixTimeSeconds(), state.Modified + 2));
            writer.Write(new byte[] { 0x2A, 0x2B, 0x0E });
            WriteVarint(writer, (ulong)inner.Length);
            writer.Write(inner.ToArray());
            writer.Write(new byte[] { 0, 0, 0 });
        }
        var result = outer.ToArray();
        return DecodeActive(result) == active ? result : null;
    }

    private static State? DecodeState(byte[] bytes)
    {
        try
        {
            if (bytes.Length > 1024)
                return null;
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Expect(reader, 0x43, 0x42, 1, 0, 0x0A, 0x02, 1, 0, 0x2A, 0x06);
            var modified = ReadVarint(reader);
            Expect(reader, 0x2A, 0x2B, 0x0E);
            var length = ReadVarint(reader);
            if (length < 5 || length > 1024 || stream.Position + (long)length + 3 != stream.Length)
                return null;
            var end = stream.Position + (long)length;
            Expect(reader, 0x43, 0x42, 1, 0);
            var active = false;
            var usable = true;
            var previous = -1;
            var fields = new SortedDictionary<int, byte[]>();
            while (true)
            {
                var start = (int)stream.Position;
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
                fields.Add(id, bytes[start..(int)stream.Position]);
            }
            if (stream.Position != end)
                return null;
            Expect(reader, 0, 0, 0);
            return new(modified, active, usable, fields);
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

    private static void WriteVarint(BinaryWriter writer, ulong value)
    {
        do
        {
            var next = (byte)(value & 127);
            value >>= 7;
            writer.Write(value == 0 ? next : (byte)(next | 128));
        } while (value != 0);
    }
}
