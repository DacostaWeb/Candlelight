namespace Candlelight.Next;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var brokerImage = string.Equals(
            Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            "Candlelight.SecureBroker",
            StringComparison.OrdinalIgnoreCase
        );
        if (brokerImage || args.Contains("--secure-broker"))
        {
            if (!brokerImage || !args.SequenceEqual(new[] { "--secure-broker" }))
                return 3;
            try
            {
                return SecureDesktopBroker.Run();
            }
            catch
            {
                return 1;
            }
        }
        var secureImage = string.Equals(
            Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            "Candlelight.Secure",
            StringComparison.OrdinalIgnoreCase
        );
        if (secureImage || args.Contains("--secure-desktop"))
        {
            if (!secureImage || !args.SequenceEqual(new[] { "--secure-desktop" }))
                return 3;
            try
            {
                return SecureDesktopHost.Run();
            }
            catch
            {
                return 1;
            } // Never create a crash/error dialog on Winlogon.
        }
        if (
            !string.Equals(
                Candlelight.Engine.InputDesktop.ThreadName,
                "Default",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return 3;
        return RunOrdinary(args);
    }

    // Keep the WinForms/control-window code out of the secure entry point's JIT.
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static int RunOrdinary(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var probe = Array.Find(args, a => a.StartsWith("--probe="));
        if (probe is not null)
        {
            using var form = new RendererProbe(probe[8..]);
            Application.Run(form);
            return form.ExitCode;
        }
        string? Option(string name) =>
            Array.Find(args, a => a.StartsWith(name + "="))?[(name.Length + 1)..];
        if (Option("--probe-startup-cover") is { } coverProbe)
            return SecureStartupCoverProbe.Run(coverProbe);
        if (Option("--probe-mixed") is { } mixedProbe)
        {
            using var form = new MixedRendererProbe(mixedProbe);
            Application.Run(form);
            return form.ExitCode;
        }
        if (Option("--probe-cursors") is { } cursorProbe)
            return CursorProbe.Run(cursorProbe);
        if (Option("--command") is { } command)
            return SendCommand(
                new(
                    command,
                    Option("--monitor"),
                    double.TryParse(
                        Option("--temperature"),
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var temperature
                    )
                        ? temperature
                        : 2700,
                    double.TryParse(
                        Option("--brightness"),
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var brightness
                    )
                        ? brightness / 100
                        : 1,
                    args.Contains("--red")
                ),
                Option("--response")
            );
        if (Option("--preview") is { } preview)
            return Preview(preview);
        using var mutex = new Mutex(true, AppContext.PipeName + ".Instance", out var first);
        if (!first)
            return SendCommand(new(args.Contains("--hidden") ? "status" : "show"), null);
        try
        {
            var path =
                Option("--settings")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Candlelight.Next",
                    "Settings.json"
                );
            var store = new ProfileStore(path, Option("--import"));
            using var context = new AppContext(store, args.Contains("--hidden"));
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(
                error.Message,
                "Candlelight",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return 1;
        }
    }

    private static int SendCommand(ControlRequest request, string? responsePath)
    {
        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                ".",
                AppContext.PipeName,
                System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.None
            );
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(request));
            var result =
                reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()
                ?? throw new IOException("Empty response.");
            if (responsePath is not null)
                File.WriteAllText(responsePath, result);
            Console.WriteLine(result);
            using var document = System.Text.Json.JsonDocument.Parse(result);
            return
                document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind != System.Text.Json.JsonValueKind.Null
                ? 1
                : 0;
        }
        catch (Exception error)
        {
            if (responsePath is not null)
                File.WriteAllText(responsePath, error.ToString());
            return 1;
        }
    }

    private static int Preview(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var store = new ProfileStore(
            Path.Combine(directory, "preview-" + Guid.NewGuid().ToString("N") + ".json")
        );
        Candlelight.Engine.DisplayDescriptor[] displays =
        [
            new("preview-internal", "Ecrã interno", "", 0, 0, 1920, 1080, true),
            new("preview-oled", "OLED", "", 1920, 0, 1920, 1080, false),
        ];
        store.EnsureDisplays(displays);
        store.Update(settings =>
        {
            settings.SelectedMonitor = "preview-internal";
            settings.Timing.Location = new("Póvoa de Varzim", 41.38215, -8.75776, "Europe/Lisbon");
            foreach (var monitor in settings.Monitors)
            {
                monitor.Scheduled = true;
                foreach (var point in settings.Timing.Points)
                    monitor.PointColors[point.Id] = new(
                        point.Time.Hour >= 22 && monitor.Id == "preview-oled"
                            ? Candlelight.Engine.ColorMode.PureRed
                            : Candlelight.Engine.ColorMode.Temperature,
                        point.Time.Hour >= 22 ? 500
                            : point.Time.Hour == 11 ? 6600
                            : 2700,
                        monitor.Id == "preview-oled" ? .15 : 1
                    );
            }
        });
        var exit = 0;
        using var form = new ControlWindow(store, null, displays);
        form.TopMost = true;
        form.Shown += async (_, _) =>
        {
            try
            {
                var fixture = store.Read();
                form.VerifyPreviewControls();
                store.Update(s =>
                {
                    s.Timing = fixture.Timing;
                    s.Monitors = fixture.Monitors;
                    s.FilterCursor = fixture.FilterCursor;
                    s.Today = null;
                });
                form.Reload();
                form.PreparePreview(false);
                await Task.Delay(250);
                File.WriteAllText(
                    Path.Combine(directory, "layout.txt"),
                    $"Dpi={form.DeviceDpi}; AutoScale={form.AutoScaleDimensions}; Current={form.CurrentAutoScaleDimensions}; Client={form.ClientSize}; Font={form.Font.Height};\n"
                        + string.Join(
                            "\n",
                            Walk(form)
                                .Select(c =>
                                    $"{c.GetType().Name}: {c.Text}; bounds={c.Bounds}; font={c.Font.SizeInPoints}; height={c.Font.Height}; preferred={c.PreferredSize}"
                                )
                        )
                );
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(
                    Path.Combine(directory, "controls.png"),
                    System.Drawing.Imaging.ImageFormat.Png
                );
                using (
                    var solar = new SolarCycleDialog(
                        fixture.Timing,
                        fixture.Monitors[0],
                        DateOnly.FromDateTime(DateTime.Now),
                        "Programa"
                    )
                )
                {
                    solar.Show(form);
                    await Task.Delay(150);
                    solar.VerifyInteraction();
                    CaptureDialog(solar, "solar.png");
                }
                using (
                    var curve = new TransitionDialog(
                        fixture.Timing.Points[0] with
                        {
                            TransitionMinutes = 90,
                            Curve = Candlelight.Engine.TransitionCurve.SlowStart,
                        },
                        fixture.Timing,
                        "Só hoje"
                    )
                )
                {
                    curve.Show(form);
                    await Task.Delay(150);
                    curve.VerifyInteraction();
                    CaptureDialog(curve, "curve.png");
                }
                form.PreparePreview(true, true);
                using (var brightness = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(brightness, new(0, 0, brightness.Width, brightness.Height));
                    brightness.Save(
                        Path.Combine(directory, "brightness.png"),
                        System.Drawing.Imaging.ImageFormat.Png
                    );
                }
                form.MinimumSize = new(0, 0);
                form.ClientSize = new(
                    (int)(900 * form.DeviceDpi / 96f),
                    Math.Min(form.ClientSize.Height, (int)(650 * form.DeviceDpi / 96f))
                );
                form.PreparePreview(false);
                using (var compact = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(compact, new(0, 0, compact.Width, compact.Height));
                    compact.Save(
                        Path.Combine(directory, "compact.png"),
                        System.Drawing.Imaging.ImageFormat.Png
                    );
                }
                foreach (
                    var control in Walk(form)
                        .Where(c =>
                            c.Visible && c is Button or CheckBox or ComboBox or NumericUpDown
                        )
                )
                {
                    var bounds = form.RectangleToClient(
                        control.RectangleToScreen(control.ClientRectangle)
                    );
                    if (!form.ClientRectangle.Contains(bounds))
                        throw new InvalidOperationException(
                            "Clipped control at compact size: " + control.Text
                        );
                }
                using (var location = new LocationDialog(fixture.Timing.Location))
                {
                    location.Show(form);
                    await Task.Delay(100);
                    foreach (
                        var control in Walk(location)
                            .Where(c =>
                                c.Visible && c is Button or ComboBox or NumericUpDown or TextBox
                            )
                    )
                        if (
                            !location.ClientRectangle.Contains(
                                location.RectangleToClient(
                                    control.RectangleToScreen(control.ClientRectangle)
                                )
                            )
                        )
                            throw new InvalidOperationException(
                                "Clipped location control: " + control.Text
                            );
                    using var locationImage = new Bitmap(location.Width, location.Height);
                    location.DrawToBitmap(
                        locationImage,
                        new(0, 0, locationImage.Width, locationImage.Height)
                    );
                    locationImage.Save(
                        Path.Combine(directory, "location.png"),
                        System.Drawing.Imaging.ImageFormat.Png
                    );
                    location.VerifyPreviewControls();
                }
                File.WriteAllText(
                    Path.Combine(directory, "verification.txt"),
                    "PASS: graph edits, shared hours, global transitions, undo, detach/rejoin, solar anchor/offset, brightness, temporary manual red, program isolation, temporary curve undo/restore, solar phases, Bézier drag/cancel/precision, schedule resume, cursor option, native layout. No real renderer started."
                );

                void CaptureDialog(Form dialog, string name)
                {
                    using var screenshot = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(screenshot, new(0, 0, screenshot.Width, screenshot.Height));
                    screenshot.Save(
                        Path.Combine(directory, name),
                        System.Drawing.Imaging.ImageFormat.Png
                    );
                    File.WriteAllText(
                        Path.Combine(directory, name + ".layout.txt"),
                        string.Join(
                            "\n",
                            Walk(dialog)
                                .Select(c =>
                                    $"{c.GetType().Name} {c.Text}: {c.Bounds}, parent={c.Parent?.ClientRectangle}"
                                )
                        )
                    );
                    foreach (
                        var control in Walk(dialog)
                            .Where(c => c.Visible && c is Button or CheckBox or NumericUpDown)
                    )
                    {
                        var bounds = dialog.RectangleToClient(
                            control.RectangleToScreen(control.ClientRectangle)
                        );
                        if (!dialog.ClientRectangle.Contains(bounds))
                            throw new InvalidOperationException(
                                "Clipped dialog control: " + control.Text
                            );
                        if (
                            control.Parent is { } parent
                            && !parent.ClientRectangle.Contains(control.Bounds)
                        )
                            throw new InvalidOperationException(
                                "Clipped dialog row: " + control.Text
                            );
                    }
                }
            }
            catch (Exception error)
            {
                exit = 1;
                File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
            }
            finally
            {
                form.Exiting = true;
                form.Close();
            }
        };
        Application.Run(form);
        return exit;
    }

    private static IEnumerable<Control> Walk(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }
}
