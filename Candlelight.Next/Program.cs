namespace Candlelight.Next;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
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
            return SendCommand(new("show"), null);
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
            var monitor = settings.Monitors[0];
            monitor.Schedule =
            [
                new(new(20, 0), new(Candlelight.Engine.ColorMode.Temperature, 3500, 1), 30),
                new(new(22, 0), new(Candlelight.Engine.ColorMode.Temperature, 2700, 1), 15),
                new(new(0, 0), new(Candlelight.Engine.ColorMode.PureRed, 500, 0.15)),
            ];
        });
        var exit = 0;
        using var form = new ControlWindow(store, null, displays);
        form.TopMost = true;
        form.Shown += async (_, _) =>
        {
            try
            {
                form.VerifyPreviewControls();
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
                File.WriteAllText(
                    Path.Combine(directory, "verification.txt"),
                    "PASS: compiled control window, 4000/2700 K edits, exact red preset, per-monitor isolation, schedule enable, cursor option. No real renderer started."
                );
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
