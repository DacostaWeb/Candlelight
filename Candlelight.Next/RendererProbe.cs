using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Candlelight.Engine;
using ColorMode = Candlelight.Engine.ColorMode;

namespace Candlelight.Next;

/// <summary>Self-owned full-screen color patches, used only by --probe.</summary>
internal sealed class RendererProbe : Form
{
    private readonly string _directory;
    private readonly DisplayDescriptor _display;
    private readonly List<object> _results = [];
    private readonly List<string> _trace = [];
    private static readonly Color[] Patches =
    [
        Color.FromArgb(64, 64, 64),
        Color.FromArgb(64, 0, 0),
        Color.FromArgb(0, 64, 0),
        Color.FromArgb(0, 0, 64),
    ];
    public int ExitCode { get; private set; }

    public RendererProbe(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        _display = DisplayCatalog.GetDisplays().First(d => d.Primary);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new(_display.Left, _display.Top, _display.Width, _display.Height);
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Shown += RunProbe;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (var i = 0; i < Patches.Length; i++)
        {
            using var brush = new SolidBrush(Patches[i]);
            var x = ClientSize.Width * (i + 0.5f) / 4;
            e.Graphics.FillRectangle(brush, x - 40, ClientSize.Height / 2f - 40, 80, 80);
        }
    }

    private async void RunProbe(object? sender, EventArgs e)
    {
        try
        {
            // A hidden background STARTUPINFO can suppress the first Form.Show.
            // Explicitly show this owned source, before any renderer covers it.
            Native.SetWindowPos(
                Handle,
                -1,
                _display.Left,
                _display.Top,
                _display.Width,
                _display.Height,
                0x50
            );
            Invalidate();
            Update();
            await Task.Delay(250);
            using var renderer = new MagnificationEngine(
                line =>
                {
                    lock (_trace)
                        _trace.Add(line);
                },
                allowDesktopEffect: false
            );
            await renderer.Ready;
            await Check(renderer, "normal", ColorProfile.Normal);
            await Check(renderer, "warm-2700", new(ColorMode.Temperature, 2700, 1));
            await Check(renderer, "red-15", new(ColorMode.PureRed, 500, 0.15));
            await Check(renderer, "red-100", new(ColorMode.PureRed, 500, 1));
            await Check(renderer, "dim-40", new(ColorMode.Temperature, 6600, 0.4));
            await renderer.ExerciseSuspendAsync(true);
            await Check(
                renderer,
                "suspended-black",
                new(ColorMode.Temperature, 2700, 1),
                new(0, 0, 0)
            );
            await renderer.ExerciseSuspendAsync(false);
            await Check(renderer, "resumed-warm", new(ColorMode.Temperature, 2700, 1));
            // Real mouse input remains mapped 1:1; do not synthesize external UI input.
            var before = await renderer.InspectAsync();
            await Task.Delay(1000);
            var after = await renderer.InspectAsync();
            var progressed =
                after.Monitors.Single(d => d.Display.Id == _display.Id).Frames
                > before.Monitors.Single(d => d.Display.Id == _display.Id).Frames;
            if (!progressed)
                throw new InvalidOperationException("Renderer stopped producing updates.");
            _results.Add(
                new
                {
                    test = "independent-message-loop",
                    passed = progressed,
                    before = before.Monitors[0].Frames,
                    after = after.Monitors[0].Frames,
                }
            );
            File.WriteAllText(
                Path.Combine(_directory, "results.json"),
                JsonSerializer.Serialize(
                    _results,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
            File.WriteAllText(
                Path.Combine(_directory, "verification.txt"),
                "PASS: real Magnification windows, rendered patch channels, red-only, brightness, independent updates.\nPhysical wake and additional monitor tests remain required."
            );
        }
        catch (Exception error)
        {
            ExitCode = 1;
            File.WriteAllText(Path.Combine(_directory, "failure.txt"), error.ToString());
            File.WriteAllText(
                Path.Combine(_directory, "results.json"),
                JsonSerializer.Serialize(
                    _results,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        finally
        {
            lock (_trace)
                File.WriteAllLines(Path.Combine(_directory, "renderer.log"), _trace);
            Close();
        }
    }

    private async Task Check(
        MagnificationEngine renderer,
        string name,
        ColorProfile profile,
        ChannelGain? overrideGain = null
    )
    {
        await renderer.ApplyAsync(_display.Id, profile);
        await Task.Delay(350);
        using var capture = new Bitmap(_display.Width, _display.Height);
        using (var graphics = Graphics.FromImage(capture))
        {
            var screen = GetDC(0);
            var target = graphics.GetHdc();
            try
            {
                if (
                    !BitBlt(
                        target,
                        0,
                        0,
                        capture.Width,
                        capture.Height,
                        screen,
                        _display.Left,
                        _display.Top,
                        0x40CC0020
                    )
                )
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                graphics.ReleaseHdc(target);
                ReleaseDC(0, screen);
            }
        }
        var gain = overrideGain ?? ChannelGain.FromProfile(profile);
        var source = Patches;
        var samples = Enumerable
            .Range(0, 4)
            .Select(i =>
            {
                var actual = capture.GetPixel(
                    (int)(_display.Width * (i + 0.5) / 4),
                    _display.Height / 2
                );
                var expected = Color.FromArgb(
                    (int)Math.Round(source[i].R * gain.Red),
                    (int)Math.Round(source[i].G * gain.Green),
                    (int)Math.Round(source[i].B * gain.Blue)
                );
                var passed =
                    Math.Abs(actual.R - expected.R) <= 3
                    && Math.Abs(actual.G - expected.G) <= 3
                    && Math.Abs(actual.B - expected.B) <= 3;
                if (profile.Mode == ColorMode.PureRed || overrideGain is not null)
                    passed &= actual.G == 0 && actual.B == 0;
                return new
                {
                    patch = i,
                    actual = new[] { (int)actual.R, actual.G, actual.B },
                    expected = new[] { (int)expected.R, expected.G, expected.B },
                    passed,
                };
            })
            .ToArray();
        _results.Add(
            new
            {
                test = name,
                samples,
                state = await renderer.InspectAsync(),
            }
        );
        if (samples.Any(s => !s.passed))
            throw new InvalidOperationException(
                $"Rendered pixels failed {name}; see results.json and the self-owned test pattern capture."
            );
        // Persist only the known, validated source patches, never desktop content
        // surrounding a test pattern or a failed capture.
        using var tiles = new Bitmap(128, 32);
        using (var graphics = Graphics.FromImage(tiles))
        {
            for (var i = 0; i < 4; i++)
            {
                var center = (int)(_display.Width * (i + 0.5) / 4);
                graphics.DrawImage(
                    capture,
                    new Rectangle(i * 32, 0, 32, 32),
                    new Rectangle(center - 16, _display.Height / 2 - 16, 32, 32),
                    GraphicsUnit.Pixel
                );
            }
        }
        for (var i = 0; i < 4; i++)
        {
            var expected = samples[i].expected;
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var actual = tiles.GetPixel(i * 32 + x, y);
                if (
                    Math.Abs(actual.R - expected[0]) > 3
                    || Math.Abs(actual.G - expected[1]) > 3
                    || Math.Abs(actual.B - expected[2]) > 3
                )
                    throw new InvalidOperationException(
                        "A test tile was obscured; no image saved."
                    );
            }
        }
        tiles.Save(Path.Combine(_directory, name + ".png"), ImageFormat.Png);
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint target,
        int x,
        int y,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation
    );
}
