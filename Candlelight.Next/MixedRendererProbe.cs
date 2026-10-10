using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Candlelight.Engine;
using Microsoft.Win32.SafeHandles;
using ColorMode = Candlelight.Engine.ColorMode;

namespace Candlelight.Next;

/// <summary>Own dark patches on two real monitors; never saves desktop images.</summary>
internal sealed class MixedRendererProbe : Form
{
    private readonly string _directory;
    private readonly IReadOnlyList<DisplayDescriptor> _displays = DisplayCatalog.GetDisplays();
    private readonly List<Form> _sources = [];
    private readonly List<object> _results = [];
    private static readonly Color[] Patches =
    [
        Color.FromArgb(64, 64, 64),
        Color.FromArgb(64, 0, 0),
        Color.FromArgb(0, 64, 0),
        Color.FromArgb(0, 0, 64),
    ];
    public int ExitCode { get; private set; }

    public MixedRendererProbe(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        Configure(this, _displays.First(d => d.Primary));
        Shown += RunProbe;
    }

    private static void Configure(Form form, DisplayDescriptor display)
    {
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new(display.Left, display.Top, display.Width, display.Height);
        form.ShowInTaskbar = false;
        form.TopMost = true;
        form.BackColor = Color.Black;
        form.Paint += (_, e) =>
        {
            for (var i = 0; i < Patches.Length; i++)
            {
                using var brush = new SolidBrush(Patches[i]);
                e.Graphics.FillRectangle(
                    brush,
                    form.ClientSize.Width * (i + .5f) / 4 - 40,
                    form.ClientSize.Height / 2f - 40,
                    80,
                    80
                );
            }
        };
    }

    private async void RunProbe(object? sender, EventArgs args)
    {
        try
        {
            if (_displays.Count != 2)
                throw new InvalidOperationException(
                    "The mixed-renderer probe requires two monitors."
                );
            foreach (var display in _displays)
            {
                var source = display.Primary ? this : new Form();
                if (!display.Primary)
                {
                    Configure(source, display);
                    _sources.Add(source);
                    source.Show();
                }
                Native.SetWindowPos(
                    source.Handle,
                    -1,
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    0x50
                );
                source.Invalidate();
                source.Update();
            }
            File.WriteAllText(
                Path.Combine(_directory, "ready.txt"),
                "Own dark patches painted on both monitors."
            );
            await Task.Delay(250);
            using var engine = new MagnificationEngine(trackInputDesktop: true);
            await engine.Ready;
            await engine.SetCursorFilteringAsync(false);
            var primary = _displays.Single(d => d.Primary);
            var secondary = _displays.Single(d => !d.Primary);
            var warm = new ColorProfile(ColorMode.Temperature, 2700, 1);
            var red = new ColorProfile(ColorMode.PureRed, 500, .15);
            await engine.ApplyProfilesAsync([(primary.Id, warm, true), (secondary.Id, red, true)]);
            await Task.Delay(450);
            var state = await engine.InspectAsync();
            if (
                state.Renderer != "DesktopWithLocalCorrections"
                || state.InputDesktopInactive
                || !state.DesktopEffectVerified
                || state.LocalSurfaces != 1
                || state.Monitors.Single(m => m.Display.Id == primary.Id).Profile != warm
                || state.Monitors.Single(m => m.Display.Id == secondary.Id).Profile != red
            )
                throw new InvalidOperationException(
                    "The mixed renderer did not preserve the two requested profiles."
                );
            _results.Add(
                new
                {
                    test = "mixed-state",
                    passed = true,
                    state,
                }
            );
            CheckPixels(secondary, ChannelGain.FromProfile(red), "red-15-local-pixels");
            // GDI capture may omit the final desktop matrix. Its native readback,
            // local pixels and mathematical composition are checked separately.
            // Physical shell appearance remains a user-confirmed gate.
            await engine.ExerciseSuspendAsync(true);
            await Task.Delay(100);
            var sleeping = await engine.InspectAsync();
            if (!sleeping.DesktopEffectVerified || sleeping.Monitors.Any(m => !m.Black))
                throw new InvalidOperationException("Mixed sleep protection failed.");
            CheckPixels(secondary, new(0, 0, 0), "suspended-local-black");
            await engine.ExerciseSuspendAsync(false);
            await Task.Delay(450);
            var resumed = await engine.InspectAsync();
            if (!resumed.DesktopEffectVerified || resumed.Monitors.Any(m => m.Black))
                throw new InvalidOperationException("Mixed resume restoration failed.");
            CheckPixels(secondary, ChannelGain.FromProfile(red), "resumed-red-15");
            await engine.ApplyProfilesAsync([(primary.Id, warm, false), (secondary.Id, red, true)]);
            await Task.Delay(350);
            var paused = await engine.InspectAsync();
            if (
                paused.DesktopGain != new ChannelGain(1, 1, 1)
                || paused.Monitors.Single(m => m.Display.Id == primary.Id).Enabled
            )
                throw new InvalidOperationException(
                    "Pausing the primary monitor affected its colors."
                );
            CheckPixels(primary, new(1, 1, 1), "paused-primary-identity");
            CheckPixels(secondary, ChannelGain.FromProfile(red), "paused-primary-red-oled");
            foreach (
                var secondaryProfile in new[]
                {
                    red,
                    new ColorProfile(ColorMode.Temperature, 4000, .15),
                }
            )
            {
                var dimWarm = warm with { Brightness = .35 };
                await engine.ApplyProfilesAsync([
                    (primary.Id, dimWarm, true),
                    (secondary.Id, secondaryProfile, true),
                ]);
                await Task.Delay(350);
                var dimState = await engine.InspectAsync();
                var shared = ChannelGain.FromProfile(dimWarm);
                if (
                    !dimState.DesktopEffectVerified
                    || dimState.DesktopGain != shared
                    || dimState.LocalSurfaces != 1
                )
                    throw new InvalidOperationException(
                        "Dim mixed profiles did not retain the expected shared matrix."
                    );
                var primaryGray = ReadPatches(primary)[0];
                var includesDesktop = Math.Abs(primaryGray.R - Math.Round(64 * shared.Red)) <= 2;
                CheckPixels(
                    primary,
                    includesDesktop ? shared : new(1, 1, 1),
                    "dim-primary-capture-model"
                );
                var desired = ChannelGain.FromProfile(secondaryProfile);
                var captured = includesDesktop
                    ? desired
                    : new ChannelGain(
                        desired.Red / shared.Red,
                        desired.Green / shared.Green,
                        desired.Blue / shared.Blue
                    );
                CheckPixels(secondary, captured, "dim-mixed-matrices-compose");
                _results.Add(
                    new
                    {
                        test = "desktop-capture-model",
                        includesDesktop,
                        dimState,
                    }
                );
            }
            await engine.ApplyProfilesAsync([(primary.Id, warm, true), (secondary.Id, warm, true)]);
            await Task.Delay(150);
            var equal = await engine.InspectAsync();
            if (
                equal.Renderer != "Desktop"
                || equal.LocalSurfaces != 0
                || !equal.DesktopEffectVerified
            )
                throw new InvalidOperationException(
                    "Equal profiles left unnecessary local surfaces."
                );
            _results.Add(
                new
                {
                    test = "equal-profiles-desktop-only",
                    passed = true,
                    state = equal,
                }
            );
            var originals = SystemCursorFilter.Ids.ToDictionary(
                id => id,
                id => CursorImage.Read(CursorNative.LoadCursor(0, (nint)id))
            );
            using var originalArrow = new OriginalCursorCopy(32512);
            using (var cursors = new SystemCursorFilter(null))
            {
                // SetSystemCursor resamples an installed bitmap on this DPI setup.
                // Compare tinting against an installed identity copy, not the
                // uninstalled source bitmap; keep alpha, geometry and hotspots strict.
                cursors.Apply(new(1, 1, 1));
                var installedIdentity = SystemCursorFilter.Ids.ToDictionary(
                    id => id,
                    id => CursorImage.Read(CursorNative.LoadCursor(0, (nint)id))
                );
                foreach (
                    var gain in new[]
                    {
                        ChannelGain.FromProfile(warm),
                        ChannelGain.FromProfile(red),
                        ChannelGain.FromProfile(warm),
                    }
                )
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    cursors.Apply(gain);
                    timer.Stop();
                    foreach (var (id, original) in installedIdentity)
                    {
                        var actual = CursorImage.Read(CursorNative.LoadCursor(0, (nint)id));
                        var expectedPixels = original.Tint(gain);
                        var validPixels =
                            actual.Pixels.Length == expectedPixels.Length
                            && Enumerable
                                .Range(0, actual.Pixels.Length / 4)
                                .All(pixel =>
                                {
                                    var offset = pixel * 4;
                                    if (actual.Pixels[offset + 3] != expectedPixels[offset + 3])
                                        return false;
                                    return true;
                                });
                        var visible = Enumerable
                            .Range(0, actual.Pixels.Length / 4)
                            .Where(pixel => actual.Pixels[pixel * 4 + 3] != 0)
                            .ToArray();
                        var actualPeaks = Enumerable
                            .Range(0, 3)
                            .Select(channel =>
                                visible.Max(pixel => (int)actual.Pixels[pixel * 4 + channel])
                            )
                            .ToArray();
                        var expectedPeaks = Enumerable
                            .Range(0, 3)
                            .Select(channel =>
                                visible.Max(pixel => (int)expectedPixels[pixel * 4 + channel])
                            )
                            .ToArray();
                        validPixels &= actualPeaks
                            .Zip(expectedPeaks)
                            .All(p => Math.Abs(p.First - p.Second) <= 2);
                        // DPI resampling blends antialiased edge RGB. Check opacity,
                        // hotspot and channel peaks rather than byte equality at edges.
                        if (gain.Green == 0 || gain.Blue == 0)
                            validPixels &= Enumerable
                                .Range(0, actual.Pixels.Length / 4)
                                .All(pixel =>
                                    (gain.Green != 0 || actual.Pixels[pixel * 4 + 1] == 0)
                                    && (gain.Blue != 0 || actual.Pixels[pixel * 4] == 0)
                                );
                        if (
                            actual.Width != original.Width
                            || actual.Height != original.Height
                            || actual.HotspotX != original.HotspotX
                            || actual.HotspotY != original.HotspotY
                            || !validPixels
                        )
                        {
                            _results.Add(
                                new
                                {
                                    test = "cursor-difference",
                                    id,
                                    original.Width,
                                    original.Height,
                                    original.HotspotX,
                                    original.HotspotY,
                                    installedWidth = actual.Width,
                                    installedHeight = actual.Height,
                                    installedHotspotX = actual.HotspotX,
                                    installedHotspotY = actual.HotspotY,
                                    expectedSample = expectedPixels.Take(32).ToArray(),
                                    actualSample = actual.Pixels.Take(32).ToArray(),
                                    differentBytes = actual
                                        .Pixels.Zip(expectedPixels)
                                        .Count(p => p.First != p.Second),
                                    actualPeaks,
                                    expectedPeaks,
                                    differentAlpha = Enumerable
                                        .Range(0, actual.Pixels.Length / 4)
                                        .Count(pixel =>
                                            actual.Pixels[pixel * 4 + 3]
                                            != expectedPixels[pixel * 4 + 3]
                                        ),
                                }
                            );
                            throw new InvalidOperationException(
                                $"Native cursor {id} failed profile switching."
                            );
                        }
                    }
                    _results.Add(
                        new
                        {
                            test = "native-cursor-profile-switch",
                            passed = true,
                            gain,
                            milliseconds = timer.Elapsed.TotalMilliseconds,
                        }
                    );
                }
                var installed = SystemCursorFilter.Ids.ToDictionary(
                    id => id,
                    id => CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint
                );
                var leasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Candlelight.Next",
                    "SystemCursorLease.json"
                );
                var leaseBefore = File.ReadAllBytes(leasePath);
                var refreshTimer = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 20; i++)
                    cursors.Refresh();
                refreshTimer.Stop();
                if (!File.ReadAllBytes(leasePath).SequenceEqual(leaseBefore))
                    throw new InvalidOperationException(
                        "Unchanged cursor refresh rewrote its lease."
                    );
                _results.Add(
                    new
                    {
                        test = "native-cursor-read-only-refresh",
                        passed = true,
                        meanMilliseconds = refreshTimer.Elapsed.TotalMilliseconds / 20,
                    }
                );
                // Simulate a standard arrow reset while a gain is already active.
                // Use the original native copy, preserving its exact DPI geometry.
                if (!CursorNative.SetSystemCursor(originalArrow.DangerousGetHandle(), 32512))
                    throw new InvalidOperationException(
                        "Could not exercise standard cursor reset."
                    );
                originalArrow.SetHandleAsInvalid(); // SetSystemCursor consumed the handle.
                if (
                    CursorImage.Read(CursorNative.LoadCursor(0, 32512)).Fingerprint
                    == installed[32512]
                )
                    throw new InvalidOperationException(
                        "Cursor reset probe did not change the arrow."
                    );
                cursors.Refresh();
                foreach (var (id, fingerprint) in installed)
                    if (
                        CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint
                        != fingerprint
                    )
                        throw new InvalidOperationException(
                            $"Cursor reset was not repaired for {id}."
                        );
                _results.Add(new { test = "native-cursor-reset-recovery", passed = true });
                cursors.Restore();
            }
            foreach (var (id, original) in originals)
                if (
                    CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint
                    != original.Fingerprint
                )
                    throw new InvalidOperationException($"Native cursor {id} was not restored.");
            await engine.SetCursorFilteringAsync(true);
            var cursorState = await engine.InspectAsync();
            if (!cursorState.SystemCursorsFiltered || cursorState.CursorError is not null)
                throw new InvalidOperationException(
                    "The renderer did not activate native cursor filtering."
                );
            await engine.SetCursorFilteringAsync(false);
            _results.Add(
                new
                {
                    test = "native-cursor-engine-and-restoration",
                    passed = true,
                    state = cursorState,
                }
            );
            engine.Dispose();
            using (
                var secureEngine = new MagnificationEngine(
                    allowDesktopEffect: false,
                    manageSystemCursors: false,
                    protectSessionLock: false,
                    trackInputDesktop: true
                )
            )
            {
                await secureEngine.Ready;
                await secureEngine.ApplyProfilesAsync([
                    (primary.Id, warm, true),
                    (secondary.Id, red, true),
                ]);
                await Task.Delay(350);
                var secureState = await secureEngine.InspectAsync();
                if (
                    secureState.InputDesktopInactive
                    || secureState.LocalSurfaces != 2
                    || secureState.Renderer != "PerMonitorWindows"
                    || secureState.SystemCursorsFiltered
                )
                    throw new InvalidOperationException(
                        "The isolated secure renderer configuration failed on the test desktop."
                    );
                CheckPixels(
                    primary,
                    ChannelGain.FromProfile(warm),
                    "secure-configuration-primary-pixels"
                );
                CheckPixels(
                    secondary,
                    ChannelGain.FromProfile(red),
                    "secure-configuration-oled-pixels"
                );
                _results.Add(
                    new
                    {
                        test = "secure-renderer-configuration-on-normal-desktop",
                        passed = true,
                        state = secureState,
                    }
                );
            }
            File.WriteAllText(
                Path.Combine(_directory, "verification.txt"),
                "PASS: mixed warm/red real monitors, one local surface, preserved profiles, exact local red channels/brightness, dim matrix composition, pause isolation, black/resume, native cursor recolor/restore, desktop matrix readback. Physical taskbar/cursor feedback remains required."
            );
        }
        catch (Exception error)
        {
            ExitCode = 1;
            File.WriteAllText(Path.Combine(_directory, "failure.txt"), error.ToString());
        }
        finally
        {
            File.WriteAllText(
                Path.Combine(_directory, "results.json"),
                JsonSerializer.Serialize(
                    _results,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
            foreach (var source in _sources)
            {
                source.Close();
                source.Dispose();
            }
            Close();
        }
    }

    private void CheckPixels(DisplayDescriptor display, ChannelGain gain, string name)
    {
        var colors = ReadPatches(display);
        var samples = Enumerable
            .Range(0, 4)
            .Select(i =>
            {
                var actual = colors[i];
                var expected = new[]
                {
                    (int)Math.Round(Patches[i].R * gain.Red),
                    (int)Math.Round(Patches[i].G * gain.Green),
                    (int)Math.Round(Patches[i].B * gain.Blue),
                };
                var rgb = new[] { (int)actual.R, actual.G, actual.B };
                var passed = rgb.Zip(expected).All(p => Math.Abs(p.First - p.Second) <= 2);
                if (gain.Green == 0)
                    passed &= actual.G == 0;
                if (gain.Blue == 0)
                    passed &= actual.B == 0;
                return new
                {
                    actual = rgb,
                    expected,
                    passed,
                };
            })
            .ToArray();
        _results.Add(
            new
            {
                test = name,
                display = display.Id,
                samples,
            }
        );
        if (samples.Any(s => !s.passed))
            throw new InvalidOperationException(
                $"Owned test patches failed {name}; see results.json."
            );
    }

    private sealed class OriginalCursorCopy : SafeHandleZeroOrMinusOneIsInvalid
    {
        public OriginalCursorCopy(uint id)
            : base(true)
        {
            SetHandle(CursorNative.CopyIcon(CursorNative.LoadCursor(0, (nint)id)));
            if (IsInvalid)
                throw new InvalidOperationException(
                    "Could not copy original cursor for the probe."
                );
        }

        protected override bool ReleaseHandle() => CursorNative.DestroyCursor(handle);
    }

    private static Color[] ReadPatches(DisplayDescriptor display)
    {
        // Capture only the centers of our four known 80px patches, in memory.
        using var capture = new Bitmap(4, 1, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(capture))
        {
            var screen = GetDC(0);
            var target = graphics.GetHdc();
            try
            {
                for (var i = 0; i < 4; i++)
                    if (
                        !BitBlt(
                            target,
                            i,
                            0,
                            1,
                            1,
                            screen,
                            display.Left + (int)(display.Width * (i + .5) / 4),
                            display.Top + display.Height / 2,
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
        return Enumerable.Range(0, 4).Select(i => capture.GetPixel(i, 0)).ToArray();
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
