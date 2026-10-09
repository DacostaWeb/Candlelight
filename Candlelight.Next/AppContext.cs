using System.IO.Pipes;
using System.Text.Json;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed record ControlRequest(
    string Command,
    string? Monitor = null,
    double Temperature = 2700,
    double Brightness = 1,
    bool Red = false
);

internal sealed class AppContext : ApplicationContext
{
    public static string PipeName =>
        "Candlelight.Next."
        + Environment.UserName
        + "."
        + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private readonly ProfileStore _store;
    private readonly ProfileController _controller;
    private readonly ControlWindow _window;
    private readonly NotifyIcon _tray;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _logGate = new();
    private readonly string _logPath;
    private bool _disposed;

    public AppContext(ProfileStore store, bool hidden)
    {
        _store = store;
        _logPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(store.Path)!,
            "Renderer.log"
        );
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_logPath)!);
        _store.EnsureDisplays(DisplayCatalog.GetDisplays());
        _controller = new(store, Log);
        _window = new(store, _controller, DisplayCatalog.GetDisplays());
        _ = _window.Handle;
        _controller.Engine.StatusChanged += snapshot =>
        {
            if (_disposed || _window.IsDisposed)
                return;
            try
            {
                _window.BeginInvoke(() => _window.ShowStatus(snapshot));
            }
            catch (InvalidOperationException) { }
        };
        var menu = new ContextMenuStrip
        {
            BackColor = ControlWindow.Surface,
            ForeColor = ControlWindow.TextColor,
        };
        menu.Items.Add("Abrir", null, (_, _) => Show());
        foreach (var preset in new AppSettings().Presets)
            menu.Items.Add(preset.Name, null, (_, _) => _window.ApplyPreset(preset.Profile));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _tray = new()
        {
            Icon = new Icon(System.IO.Path.Combine(AppContextBase(), "favicon.ico")),
            Text = "Candlelight 0.2 · versão de teste",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => Show();
        _ = ServeAsync();
        if (!hidden)
            Show();
        Log("Started 0.2.2. UI and renderer have independent lifetimes. Settings=" + store.Path);
    }

    private static string AppContextBase() => System.AppContext.BaseDirectory;

    private void Show()
    {
        _window.Show();
        _window.Activate();
    }

    private void Log(string line)
    {
        lock (_logGate)
        {
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 512000)
                File.Move(_logPath, _logPath + ".old", true);
            File.AppendAllText(
                _logPath,
                DateTimeOffset.Now.ToString("O") + " " + line + Environment.NewLine
            );
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
                );
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_stop.Token);
                if (line is null || line.Length > 16000)
                    continue;
                var request = JsonSerializer.Deserialize<ControlRequest>(line, ProfileStore.Json)!;
                var result = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _window.BeginInvoke(async () =>
                {
                    try
                    {
                        result.SetResult(await HandleAsync(request));
                    }
                    catch (Exception error)
                    {
                        result.SetResult(new { error = error.Message });
                    }
                });
                await writer.WriteLineAsync(
                    JsonSerializer
                        .Serialize(
                            await result.Task.WaitAsync(TimeSpan.FromSeconds(8)),
                            ProfileStore.Json
                        )
                        .Replace("\r", "")
                        .Replace("\n", "")
                );
                if (request.Command == "stop")
                    _window.BeginInvoke(ExitThread);
            }
            catch (Exception error) when (!_stop.IsCancellationRequested)
            {
                Log("Control request failed: " + error.Message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<object> HandleAsync(ControlRequest request)
    {
        switch (request.Command)
        {
            case "show":
                Show();
                break;
            case "hide":
                _window.Hide();
                break;
            case "set":
                _store.Update(settings =>
                {
                    var id = request.Monitor ?? settings.SelectedMonitor ?? settings.Monitors[0].Id;
                    var monitor = settings.Monitors.Single(m => m.Id == id);
                    monitor.Manual = new ColorProfile(
                        request.Red ? ColorMode.PureRed : ColorMode.Temperature,
                        request.Temperature,
                        request.Brightness
                    ).Validate();
                    monitor.Scheduled = false;
                    monitor.Enabled = true;
                });
                await _controller.ApplyNowAsync();
                _window.Reload();
                break;
            case "pause":
                _store.Update(settings =>
                {
                    var id = request.Monitor ?? settings.SelectedMonitor ?? settings.Monitors[0].Id;
                    settings.Monitors.Single(m => m.Id == id).Enabled = false;
                });
                await _controller.ApplyNowAsync();
                _window.Reload();
                break;
            case "status":
            case "stop":
                break;
            case "probe-suspend":
                await _controller.Engine.ExerciseSuspendAsync(true);
                break;
            case "probe-resume":
                await _controller.Engine.ExerciseSuspendAsync(false);
                break;
            case "cursor-on":
            case "cursor-off":
                _store.Update(settings => settings.FilterCursor = request.Command == "cursor-on");
                await _controller.ApplyNowAsync();
                _window.Reload();
                break;
            default:
                throw new ArgumentException("Unknown command.");
        }
        return new
        {
            version = "0.2.2",
            windowVisible = _window.Visible,
            error = _controller.Error,
            engine = await _controller.Engine.InspectAsync(),
            settings = _store.Read(),
        };
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeResources();
        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Cancel();
        _controller.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _window.Exiting = true;
        _window.Close();
        _window.Dispose();
    }
}
