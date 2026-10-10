using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Candlelight.Engine;

public sealed record EngineMonitorState(
    DisplayDescriptor Display,
    ColorProfile Profile,
    bool Enabled,
    bool Black,
    long Frames,
    string? Error
);

public sealed record EngineSnapshot(
    IReadOnlyList<EngineMonitorState> Monitors,
    bool Sleeping,
    bool Locked,
    long Timestamp,
    string Renderer,
    bool DesktopEffectVerified,
    bool SystemCursorsFiltered,
    string? CursorError,
    ChannelGain? DesktopGain,
    int LocalSurfaces,
    string? CursorMonitor
)
{
    public bool UiAccessEnabled { get; } = DesktopAccess.UiAccessEnabled;
    public bool InputDesktopInactive { get; init; }
}

/// <summary>
/// A desktop color renderer with its own thread, message loop and native windows.
/// No dependency on a settings window, Night Light, gamma ramps or private Windows APIs.
/// API success/readback is diagnostic only; it does not certify scanout pixels.
/// </summary>
public sealed class MagnificationEngine : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly TaskCompletionSource _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly Dictionary<string, Presenter> _presenters = new();
    private readonly Dictionary<string, (ColorProfile Profile, bool Enabled)> _profiles = new();
    private readonly Native.WndProc _procedure;
    private readonly string _className =
        "Candlelight.ColorRenderer." + Guid.NewGuid().ToString("N");
    private readonly Action<string>? _log;
    private nint _window,
        _powerNotification;
    private bool _sleeping,
        _locked,
        _displayOff;
    private long _resumeAt,
        _lastStatus,
        _lastCursorRefresh,
        _lastWake;
    private volatile bool _disposed;
    private bool _initialized;
    private readonly bool _allowDesktopEffect;
    private bool _desktopUnavailable;
    private Native.ColorEffect _desktopPrevious,
        _desktopEffect;
    private ChannelGain? _desktopGain;
    private DesktopRenderPlan _plan = new(null, []);
    private string? _cursorMonitor;
    private bool _desktopBlack;
    private long _desktopUpdates;
    private SystemCursorFilter? _systemCursors;
    private bool _filterCursors = true;
    private string? _cursorError;
    private readonly bool _manageSystemCursors;
    private readonly bool _protectSessionLock;
    private readonly bool _trackInputDesktop;
    private string? _threadDesktop;
    private bool _desktopInactive;

    public event Action<EngineSnapshot>? StatusChanged;
    public Task Ready => _started.Task;

    public MagnificationEngine(
        Action<string>? log = null,
        bool allowDesktopEffect = true,
        bool manageSystemCursors = true,
        bool protectSessionLock = true,
        bool trackInputDesktop = false
    )
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The color renderer requires 64-bit Windows.");
        _log = log;
        _allowDesktopEffect = allowDesktopEffect;
        _manageSystemCursors = manageSystemCursors;
        _protectSessionLock = protectSessionLock;
        _trackInputDesktop = trackInputDesktop;
        _procedure = WindowProcedure;
        _thread = new Thread(Run) { IsBackground = true, Name = "Candlelight color renderer" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public async Task ApplyAsync(string displayId, ColorProfile profile, bool enabled = true) =>
        await ApplyProfilesAsync([(displayId, profile, enabled)]);

    public async Task ApplyProfilesAsync(
        IEnumerable<(string DisplayId, ColorProfile Profile, bool Enabled)> profiles
    )
    {
        var validated = profiles
            .Select(p => (p.DisplayId, Profile: p.Profile.Validate(), p.Enabled))
            .ToArray();
        await DispatchAsync(() =>
        {
            foreach (var p in validated)
                _profiles[p.DisplayId] = (p.Profile, p.Enabled);
            SynchronizeDisplays();
            PublishStatus();
        });
    }

    public async Task<EngineSnapshot> InspectAsync()
    {
        EngineSnapshot? snapshot = null;
        await DispatchAsync(() => snapshot = CreateSnapshot());
        return snapshot!;
    }

    public Task RefreshDisplaysAsync() => DispatchAsync(SynchronizeDisplays);

    public Task SetCursorFilteringAsync(bool enabled) =>
        DispatchAsync(() =>
        {
            _filterCursors = enabled;
            UpdateSystemCursors();
            PublishStatus();
        });

    // The probe invokes the same paths as native power notifications, without
    // suspending the user's PC. This does not replace a physical wake test.
    internal Task ExerciseSuspendAsync(bool sleeping) =>
        DispatchAsync(() =>
        {
            _sleeping = sleeping;
            if (sleeping)
                Protect("Probe suspend");
            else
                Wake("Probe resume");
        });

    private async Task DispatchAsync(Action command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Ready;
        ObjectDisposedException.ThrowIf(_disposed, this);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                command();
                complete.SetResult();
            }
            catch (Exception error)
            {
                complete.SetException(error);
            }
        });
        if (!Native.PostMessage(_window, Native.CommandMessage, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        try
        {
            if (!Native.MagInitialize())
                throw Error("MagInitialize");
            _initialized = true;
            _threadDesktop = InputDesktop.ThreadName;
            if (_manageSystemCursors)
                _systemCursors = new(_log);
            var windowClass = new Native.WindowClass
            {
                Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
                Procedure = _procedure,
                Instance = Native.GetModuleHandle(null),
                Background = Native.GetStockObject(4), // BLACK_BRUSH
                Name = _className,
            };
            if (Native.RegisterClassEx(ref windowClass) == 0)
                throw Error("RegisterClassEx");
            // A hidden top-level window receives power/display/session broadcasts.
            _window = Native.CreateWindowEx(
                0x08000080,
                _className,
                null,
                0x80000000,
                0,
                0,
                1,
                1,
                0,
                0,
                windowClass.Instance,
                0
            );
            if (_window == 0)
                throw Error("Create renderer window");
            var displayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");
            _powerNotification = Native.RegisterPowerSettingNotification(
                _window,
                ref displayState,
                0
            );
            if (_powerNotification == 0 || !Native.WTSRegisterSessionNotification(_window, 0))
                throw Error("Register power/session notifications");
            if (Native.SetTimer(_window, 1, 16, 0) == 0)
                throw Error("Set renderer timer");
            _started.SetResult();
            _log?.Invoke(
                "Renderer ready; documented Magnification API; 1x; no gamma/Night Light writes."
            );
            while (true)
            {
                var received = Native.GetMessage(out var message, 0, 0, 0);
                if (received == 0)
                    break;
                if (received == -1)
                    throw Error("GetMessage");
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }
        catch (Exception error)
        {
            _started.TrySetException(error);
            _log?.Invoke("Renderer stopped: " + error);
        }
        finally
        {
            RestoreDesktopEffect();
            _systemCursors?.Dispose();
            _systemCursors = null;
            foreach (var presenter in _presenters.Values)
                presenter.Dispose();
            _presenters.Clear();
            if (_powerNotification != 0)
                Native.UnregisterPowerSettingNotification(_powerNotification);
            if (_window != 0)
            {
                Native.KillTimer(_window, 1);
                Native.WTSUnRegisterSessionNotification(_window);
                Native.DestroyWindow(_window);
            }
            Native.UnregisterClass(_className, Native.GetModuleHandle(null));
            if (_initialized)
                Native.MagUninitialize();
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == Native.CommandMessage)
            {
                while (_commands.TryDequeue(out var command))
                    command();
                return 0;
            }
            if (message == 0x0010 && window == _window) // WM_CLOSE
            {
                Native.PostQuitMessage(0);
                return 0;
            }
            if (message == 0x0113 && window == _window) // WM_TIMER
            {
                Tick();
                return 0;
            }
            if (message == 0x007E && window == _window) // WM_DISPLAYCHANGE
                SynchronizeDisplays();
            if (message == 0x02B1 && window == _window && _protectSessionLock) // WM_WTSSESSION_CHANGE
            {
                if (wParam == 7)
                {
                    _locked = true;
                    Protect("Session locked");
                }
                if (wParam == 8)
                {
                    _locked = false;
                    Wake("Session unlocked");
                }
            }
            if (message == 0x0218 && window == _window) // WM_POWERBROADCAST
            {
                if (wParam == 4)
                {
                    _sleeping = true;
                    Protect("Suspending");
                }
                else if (wParam is 6 or 7 or 0x12)
                {
                    _sleeping = false;
                    Wake("Resuming");
                }
                else if (wParam == 0x8013 && lParam != 0)
                {
                    var setting = Marshal.PtrToStructure<Guid>(lParam);
                    if (
                        setting == new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47")
                        && Marshal.ReadInt32(lParam, 16) == 4
                    )
                    {
                        var state = Marshal.ReadInt32(lParam, 20);
                        if (state == 0)
                        {
                            _displayOff = true;
                            Protect("Display off");
                        }
                        else if (_displayOff)
                        {
                            _displayOff = false;
                            _sleeping = false;
                            Wake("Display on");
                        }
                    }
                }
                return 1;
            }
            if (message == 0x0021) // WM_MOUSEACTIVATE
                return 3; // MA_NOACTIVATE
            if (message == 0x0084) // WM_NCHITTEST
                return -1; // HTTRANSPARENT
        }
        catch (Exception error)
        {
            _log?.Invoke("Renderer event failed: " + error.Message);
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    private bool Black =>
        _sleeping || _locked || _displayOff || Environment.TickCount64 < _resumeAt;

    private void Protect(string reason)
    {
        _lastWake = 0;
        _resumeAt = 0;
        if (_desktopInactive)
            return;
        Native.SetTimer(_window, 1, 1000, 0);
        foreach (var presenter in _presenters.Values)
            presenter.SetProfile(presenter.Profile, presenter.Gain, true);
        if (_desktopGain is not null)
            SetDesktopEffect(_desktopGain, true);
        UpdateSystemCursors();
        Native.DwmFlush();
        _log?.Invoke(reason + "; existing renderers kept black.");
        PublishStatus();
    }

    private void Wake(string reason)
    {
        // Coalesce notifications; never remove a cover and expose an unfiltered desktop.
        var now = Environment.TickCount64;
        if (_lastWake == 0 || now - _lastWake > 2000)
            _resumeAt = Environment.TickCount64 + 300;
        _lastWake = now;
        Native.SetTimer(_window, 1, _sleeping || _displayOff || _locked ? 1000u : 16u, 0);
        SynchronizeDisplays();
        _log?.Invoke(reason + "; refreshing the same renderer surfaces.");
    }

    private void SynchronizeDisplays()
    {
        if (_desktopInactive)
            return;
        var displays = DisplayCatalog.GetDisplays();
        _plan = DesktopRenderPlan.Create(
            displays,
            _profiles,
            _allowDesktopEffect && !_desktopUnavailable
        );
        if (_plan.DesktopGain is { } shared)
        {
            try
            {
                if (_desktopGain is null)
                {
                    if (!Native.MagGetFullscreenColorEffect(out _desktopPrevious))
                        throw Error("Read previous desktop filter");
                }
                SetDesktopEffect(shared, Black);
            }
            catch (Win32Exception error)
            {
                // A rejected desktop call must not leave the whole monitor
                // unfiltered. Keep the local renderer with its explicit shell
                // limitation instead of repeating an unavailable API forever.
                _desktopUnavailable = true;
                _log?.Invoke("Desktop filter unavailable; using local surfaces: " + error);
                RestoreDesktopEffect();
                _plan = DesktopRenderPlan.Create(displays, _profiles, false);
            }
        }
        var wanted = _plan.Monitors.Where(m => m.NeedsSurface).ToArray();
        foreach (var monitor in wanted)
        {
            var display = monitor.Display;
            if (!_presenters.TryGetValue(display.Id, out var presenter))
            {
                presenter = new Presenter(display, _className);
                _presenters.Add(display.Id, presenter);
            }
            presenter.SetBounds(display);
            presenter.SetProfile(monitor.Profile, monitor.LocalGain, Black);
        }
        foreach (var id in _presenters.Keys.Except(wanted.Select(m => m.Display.Id)).ToArray())
        {
            _presenters[id].Dispose();
            _presenters.Remove(id);
        }
        // Exclude every renderer host to prevent recursive capture across monitors.
        var excluded = _presenters.Values.Select(p => p.Host).Append(_window).ToArray();
        foreach (var presenter in _presenters.Values)
        {
            presenter.Exclude(excluded);
            presenter.Render();
            presenter.Show();
        }
        if (_plan.DesktopGain is null)
            RestoreDesktopEffect();
        UpdateSystemCursors();
        PublishStatus();
    }

    private void SetDesktopEffect(ChannelGain gain, bool black)
    {
        var effect = Native.ColorEffect.FromGain(black ? new(0, 0, 0) : gain);
        if (!Native.MagSetFullscreenColorEffect(ref effect))
            throw Error("Set desktop color filter");
        _desktopGain = gain;
        _desktopBlack = black;
        _desktopEffect = effect;
        _desktopUpdates++;
        UpdateSystemCursors();
    }

    private void UpdateSystemCursors()
    {
        try
        {
            var monitor = Native.GetCursorPos(out var cursor) ? _plan.At(cursor.X, cursor.Y) : null;
            _cursorMonitor = monitor?.Display.Id;
            var gain = monitor is null
                ? new ChannelGain(1, 1, 1)
                : _plan.CursorGainAt(cursor.X, cursor.Y, _systemCursors?.BoundaryMargin ?? 64);
            var black = Black && monitor?.Enabled == true;
            if (_filterCursors && !_locked && (black || gain != new ChannelGain(1, 1, 1)))
                _systemCursors?.Apply(black ? new(0, 0, 0) : gain);
            else
                _systemCursors?.Restore();
            _cursorError = null;
        }
        catch (Exception error)
        {
            _cursorError = "Não foi possível filtrar o ponteiro.";
            _log?.Invoke("Cursor filter failed: " + error);
        }
    }

    private void RestoreDesktopEffect()
    {
        if (_desktopGain is null)
            return;
        // Leave a newer effect from another application alone.
        if (
            Native.MagGetFullscreenColorEffect(out var current)
            && current.Values.SequenceEqual(_desktopEffect.Values)
        )
            Native.MagSetFullscreenColorEffect(ref _desktopPrevious);
        _desktopGain = null;
    }

    private void Tick()
    {
        if (_trackInputDesktop)
        {
            var inactive = !InputDesktop.IsActive(_threadDesktop);
            if (inactive != _desktopInactive)
            {
                _desktopInactive = inactive;
                if (inactive)
                {
                    foreach (var presenter in _presenters.Values)
                        presenter.Hide();
                    Native.SetTimer(_window, 1, 100, 0);
                    _log?.Invoke("Input desktop changed; renderer updates suspended.");
                }
                else
                {
                    Native.SetTimer(_window, 1, Black ? 1000u : 16u, 0);
                    SynchronizeDisplays();
                    _log?.Invoke("Input desktop restored; profiles reapplied.");
                }
            }
            if (_desktopInactive)
                return;
        }
        if (_desktopGain is not null)
        {
            if (
                _desktopBlack != Black
                || !Native.MagGetFullscreenColorEffect(out var effect)
                || !effect.Values.SequenceEqual(_desktopEffect.Values)
            )
                SetDesktopEffect(_desktopGain, Black);
        }
        UpdateSystemCursors();
        // DPI/display transitions can replace a standard cursor after the gain
        // was applied. Detect that independently of the slower status interval.
        // An unchanged table is read-only; no cursor replacement or lease write.
        if (Environment.TickCount64 - _lastCursorRefresh >= 100)
        {
            try
            {
                _systemCursors?.Refresh();
            }
            catch (Exception error)
            {
                _cursorError = "Não foi possível atualizar o ponteiro.";
                _log?.Invoke(error.ToString());
            }
            _lastCursorRefresh = Environment.TickCount64;
        }
        var black = Black;
        foreach (var presenter in _presenters.Values)
        {
            if (presenter.Black != black)
                presenter.SetProfile(presenter.Profile, presenter.Gain, black);
            if (!_sleeping && !_displayOff && !_locked)
                presenter.Render();
        }
        if (Environment.TickCount64 - _lastStatus >= 1000)
        {
            foreach (var presenter in _presenters.Values)
                presenter.VerifyAndRaise();
            _lastStatus = Environment.TickCount64;
            PublishStatus();
        }
    }

    private EngineSnapshot CreateSnapshot() =>
        new(
            _plan
                .Monitors.Select(m => new EngineMonitorState(
                    m.Display,
                    m.Profile,
                    m.Enabled,
                    m.Enabled && Black,
                    _presenters.TryGetValue(m.Display.Id, out var p) ? p.Frames : _desktopUpdates,
                    p?.Error
                ))
                .ToArray(),
            _sleeping,
            _locked,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            _desktopGain is not null
                ? (_presenters.Count == 0 ? "Desktop" : "DesktopWithLocalCorrections")
                : "PerMonitorWindows",
            _desktopGain is not null
                && Native.MagGetFullscreenColorEffect(out var current)
                && current.Values.SequenceEqual(_desktopEffect.Values),
            _systemCursors?.Active == true,
            _cursorError,
            _desktopGain,
            _presenters.Count,
            _cursorMonitor
        )
        {
            InputDesktopInactive = _desktopInactive,
        };

    private void PublishStatus()
    {
        try
        {
            StatusChanged?.Invoke(CreateSnapshot());
        }
        catch (Exception error)
        {
            _log?.Invoke("Status subscriber failed: " + error.Message);
        }
    }

    internal static Win32Exception Error(string operation) =>
        new(Marshal.GetLastWin32Error(), operation + " failed.");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_window != 0)
            Native.PostMessage(_window, 0x0010, 0, 0);
        if (Thread.CurrentThread != _thread && !_thread.Join(TimeSpan.FromSeconds(5)))
            _log?.Invoke(
                "Renderer shutdown timed out; its windows will close when the process exits."
            );
    }

    private sealed class Presenter : IDisposable
    {
        public DisplayDescriptor Display { get; private set; }
        public nint Host { get; }
        private readonly nint _magnifier;
        public ColorProfile Profile { get; private set; } = ColorProfile.Normal;
        public ChannelGain Gain { get; private set; } = new(1, 1, 1);
        public bool Black { get; private set; }
        public long Frames { get; private set; }
        public string? Error { get; private set; }
        private Native.ColorEffect _effect;
        private bool _shown;

        public Presenter(DisplayDescriptor display, string hostClass)
        {
            Display = display;
            Host = Native.CreateWindowEx(
                0x080800A8,
                hostClass,
                "Candlelight color surface",
                0x82000000,
                display.Left,
                display.Top,
                display.Width,
                display.Height,
                0,
                0,
                Native.GetModuleHandle(null),
                0
            );
            if (Host == 0)
                throw MagnificationEngine.Error("Create color surface");
            try
            {
                if (!Native.SetLayeredWindowAttributes(Host, 0, 255, 2))
                    throw MagnificationEngine.Error("Set surface opacity");
                _magnifier = Native.CreateWindowEx(
                    0,
                    "Magnifier",
                    null,
                    0x50000000u, // Keep the native hardware pointer; never capture it into frames.
                    0,
                    0,
                    display.Width,
                    display.Height,
                    Host,
                    0,
                    Native.GetModuleHandle(null),
                    0
                );
                if (_magnifier == 0)
                    throw MagnificationEngine.Error("Create magnifier control");
                var identity = Native.Transform.Identity;
                if (!Native.MagSetWindowTransform(_magnifier, ref identity))
                    throw MagnificationEngine.Error("Set 1x transform");
            }
            catch
            {
                Native.DestroyWindow(Host);
                throw;
            }
        }

        public void SetBounds(DisplayDescriptor display)
        {
            if (Display == display)
                return;
            Display = display;
            Native.SetWindowPos(
                Host,
                -1,
                display.Left,
                display.Top,
                display.Width,
                display.Height,
                0x10
            );
            Native.SetWindowPos(_magnifier, 0, 0, 0, display.Width, display.Height, 0x14);
        }

        public void SetProfile(ColorProfile profile, ChannelGain gain, bool black)
        {
            Profile = profile;
            Gain = gain;
            Black = black;
            _effect = Native.ColorEffect.FromGain(gain);
            if (!Native.MagSetColorEffect(_magnifier, ref _effect))
            {
                Error = "Não foi possível aplicar o filtro de cor.";
                throw MagnificationEngine.Error("MagSetColorEffect");
            }
            Error = null;
            // Keep the opaque BLACK_BRUSH host in place. A zero color matrix can
            // leave a cached black image after restoration on this driver.
            // Hide/show only its magnifier child, with the real matrix prepared.
            Native.ShowWindow(_magnifier, black ? 0 : 5);
            if (!black)
                Native.SetWindowPos(_magnifier, 0, 0, 0, 0, 0, 0x57);
            else
            {
                Native.InvalidateRect(Host, 0, true);
                Native.UpdateWindow(Host);
            }
            Native.InvalidateRect(_magnifier, 0, true);
            Native.UpdateWindow(_magnifier);
        }

        public void Exclude(nint[] windows)
        {
            if (!Native.MagSetWindowFilterList(_magnifier, 0, windows.Length, windows))
                throw MagnificationEngine.Error("Exclude renderer surfaces");
        }

        public void Render()
        {
            var source = new Native.Rect
            {
                Left = Display.Left,
                Top = Display.Top,
                Right = Display.Left + Display.Width,
                Bottom = Display.Top + Display.Height,
            };
            if (Native.MagSetWindowSource(_magnifier, source))
            {
                Frames++;
                // Follow the documented windowed magnifier sample: reclaim the
                // topmost position on each refresh, including while shell menus
                // or an auto-hidden taskbar are being shown.
                Native.SetWindowPos(Host, -1, 0, 0, 0, 0, 0x13);
                // The documented sample explicitly repaints after changing the
                // source. An unchanged source can otherwise retain a black frame.
                Native.InvalidateRect(_magnifier, 0, true);
            }
            else
                Error = "Não foi possível atualizar a imagem deste monitor.";
        }

        public void Show()
        {
            if (_shown)
                return;
            Native.ShowWindow(Host, 4); // SW_SHOWNOACTIVATE
            // A background launch can specify SW_HIDE in STARTUPINFO. Explicit
            // SHOWWINDOW prevents that hint from hiding the first color surface.
            Native.SetWindowPos(Host, -1, 0, 0, 0, 0, 0x53);
            _shown = true;
        }

        public void Hide()
        {
            Native.ShowWindow(Host, 0);
            _shown = false;
        }

        public void VerifyAndRaise()
        {
            if (
                !Native.MagGetColorEffect(_magnifier, out var current)
                || !current.Values.SequenceEqual(_effect.Values)
            )
            {
                if (!Native.MagSetColorEffect(_magnifier, ref _effect))
                    Error = "O filtro de cor precisa de ser reiniciado.";
            }
            Native.SetWindowPos(Host, -1, 0, 0, 0, 0, 0x13);
        }

        public void Dispose() => Native.DestroyWindow(Host);
    }
}
