using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Candlelight.Next;

internal static class SecureStartupCoverProbe
{
    internal static int Run(string path)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            using var cover = new SecureStartupCover();
            cover.Ready.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            var readyMilliseconds = watch.ElapsedMilliseconds;
            Thread.Sleep(100);
            var samples = Screen
                .AllScreens.Select(screen =>
                {
                    using var pixel = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(pixel))
                        graphics.CopyFromScreen(
                            screen.Bounds.Left + 20,
                            screen.Bounds.Top + 20,
                            0,
                            0,
                            new Size(1, 1)
                        );
                    var color = pixel.GetPixel(0, 0);
                    return new
                    {
                        screen.DeviceName,
                        black = color.R == 0 && color.G == 0 && color.B == 0,
                    };
                })
                .ToArray();
            // Don't dispose yet: simulate a stalled renderer and verify the native timeout.
            Thread.Sleep(2300);
            var released = cover.Windows.Length == 0;
            var passed = samples.All(s => s.black) && released;
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    new
                    {
                        passed,
                        readyMilliseconds,
                        samples,
                        timeoutReleased = released,
                    }
                )
            );
            return passed ? 0 : 1;
        }
        catch (Exception error)
        {
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(new { passed = false, error = error.ToString() })
            );
            return 1;
        }
    }
}
