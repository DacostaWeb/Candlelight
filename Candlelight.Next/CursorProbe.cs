using System.Runtime.InteropServices;
using System.Text.Json;
using Candlelight.Engine;

namespace Candlelight.Next;

/// <summary>Tests cursor bitmaps and handles only; never changes the system cursor table.</summary>
internal static class CursorProbe
{
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadTrails(uint action, uint parameter, out uint trails, uint flags);

    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            if (!ReadTrails(0x5E, 0, out var trails, 0))
                throw MagnificationEngine.Error("Read mouse trail preference");
            var shapes = new Dictionary<uint, object>();
            foreach (var id in SystemCursorFilter.Ids)
            {
                var source = CursorImage.Read(CursorNative.LoadCursor(0, (nint)id));
                foreach (
                    var gain in new[]
                    {
                        new ChannelGain(1, 1, 1),
                        ChannelGain.FromProfile(new(ColorMode.Temperature, 2700, 1)),
                        new(0.15, 0, 0),
                    }
                )
                {
                    var cursor = source.Create(gain);
                    try
                    {
                        var copy = CursorImage.Read(cursor);
                        if (
                            copy.Width != source.Width
                            || copy.Height != source.Height
                            || copy.HotspotX != source.HotspotX
                            || copy.HotspotY != source.HotspotY
                            || !copy.Pixels.SequenceEqual(source.Tint(gain))
                        )
                            throw new InvalidOperationException(
                                $"Cursor {id}: native roundtrip changed dimensions, hotspot, alpha or color."
                            );
                    }
                    finally
                    {
                        CursorNative.DestroyCursor(cursor);
                    }
                }
                shapes[id] = new
                {
                    source.Fingerprint,
                    source.Width,
                    source.Height,
                    source.HotspotX,
                    source.HotspotY,
                };
            }
            File.WriteAllText(
                Path.Combine(directory, "results.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        passed = true,
                        mouseTrails = trails,
                        shapes,
                    },
                    ProfileStore.Json
                )
            );
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            return 1;
        }
    }
}
