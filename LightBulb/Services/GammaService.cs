using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LightBulb.Core;
using LightBulb.PlatformInterop;
using PowerKit;
using PowerKit.Extensions;
using Monitor = LightBulb.PlatformInterop.Monitor;

namespace LightBulb.Services;

public sealed class GammaService : IDisposable
{
    internal sealed record DisplayContext(DisplayInfo Info, IGammaDevice Device);

    private readonly Func<IReadOnlyList<DisplayContext>>? _enumerateOverride;
    private readonly Func<long> _clock;
    private readonly bool _runRecoveryTimer;
    private readonly Func<bool> _nightLightActive;

    private readonly SettingsService _settings;
    private readonly object _sync = new();
    private readonly IDisposable _events;
    private readonly System.Threading.Timer _recoveryTimer;
    private readonly System.Threading.Timer _watchdogTimer;
    private IReadOnlyList<DisplayContext> _contexts = [];
    private IReadOnlyList<DisplayInfo> _displays = [];
    private bool _contextsValid;
    private bool _isUpdating;
    private bool _isRecovering;
    private bool _isDisposed;
    private bool _displayOff;
    private bool _hasRequest;
    private bool _protectedHandover;
    private long _recoveryDeadline;
    private long _handoverDeadline;
    private long _lastInvalidation;
    private long _lastUpdate;
    private IReadOnlyDictionary<string, ColorConfiguration> _requested =
        new Dictionary<string, ColorConfiguration>();
    private readonly Dictionary<string, ColorConfiguration> _applied = new(
        StringComparer.OrdinalIgnoreCase
    );
    private ColorConfiguration _fallback = ColorConfiguration.Default;
    private string? _lastTrace;

    public GammaService(SettingsService settings)
        : this(settings, null, () => Environment.TickCount64, true) { }

    internal GammaService(
        SettingsService settings,
        Func<IReadOnlyList<DisplayContext>>? enumerate,
        Func<long> clock,
        bool registerEvents,
        Func<bool>? nightLightActive = null
    )
    {
        _settings = settings;
        _enumerateOverride = enumerate;
        _clock = clock;
        _lastInvalidation = clock() - 5000;
        _runRecoveryTimer = registerEvents;
        _nightLightActive =
            nightLightActive
            ?? (
                () =>
                    registerEvents
                    && !StartOptions.Current.IsPreview
                    && NightLight.ReadActive() is true
            );
        _recoveryTimer = new System.Threading.Timer(
            _ => RecoveryTick(),
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
        _watchdogTimer = new System.Threading.Timer(
            _ => WatchdogTick(),
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
        _events = !registerEvents
            ? Disposable.Null
            : Disposable.Merge(
                SystemHook.TryRegister(SystemHook.Ids.ForegroundWindowChanged, InvalidateGamma)
                    ?? Disposable.Null,
                PowerSettingNotification.TryRegisterDisplayState(
                    PowerSettingNotification.Ids.ConsoleDisplayStateChanged,
                    state => OnDisplayState(state, "console")
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegisterDisplayState(
                    PowerSettingNotification.Ids.SessionDisplayStatusChanged,
                    state => OnDisplayState(state, "session")
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegisterDisplayState(
                    PowerSettingNotification.Ids.MonitorPowerStateChanged,
                    state => OnDisplayState(state, "monitor")
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegister(
                    PowerSettingNotification.Ids.PowerSavingStatusChanged,
                    InvalidateGamma
                ) ?? Disposable.Null,
                ResumeNotification.Register(
                    () => Recover("resume/session"),
                    () => OnDisplayState(0, "suspend"),
                    locked => SessionLockChanged?.Invoke(locked)
                ),
                SystemEvent.Register(
                    SystemEvent.Ids.DisplayChanged,
                    () => Recover("display change")
                ),
                SystemEvent.Register(SystemEvent.Ids.PaletteChanged, InvalidateDeviceContexts),
                SystemEvent.Register(SystemEvent.Ids.SettingsChanged, InvalidateDeviceContexts),
                SystemEvent.Register(SystemEvent.Ids.SystemColorsChanged, InvalidateDeviceContexts)
            );
    }

    // Runs on the window's message thread, before cached values are reapplied.
    public event Action? RecoveryRequested;
    public event Action? DisplaysChanged;
    public event Action? ApplyStatusChanged;
    public event Action? SleepTransitionRequested;
    public event Action? WakeTransitionRequested;
    public event Action<bool>? SessionLockChanged;
    public IReadOnlyList<string> FailedDisplayIds { get; private set; } = [];
    public IReadOnlyDictionary<string, string> FailureReasons { get; private set; } =
        new Dictionary<string, string>();

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return [];
            EnsureContexts();
            return _displays;
        }
    }

    private void EnsureContexts()
    {
        if (_contextsValid)
            return;

        _contextsValid = true;
        Disposable.Merge(_contexts.Select(c => c.Device)).Dispose();
        _applied.Clear();

        if (_enumerateOverride is not null)
        {
            _contexts = _enumerateOverride();
            _displays = _contexts.Select(c => c.Info).ToArray();
            return;
        }

        if (StartOptions.Current.IsPreview)
        {
            _displays =
            [
                new(
                    "preview-internal",
                    "DISPLAY1",
                    "Ecrã interno",
                    true,
                    new Rect(0, 0, 1920, 1080)
                ),
                new(
                    "preview-oled",
                    "DISPLAY2",
                    "OLED externo",
                    false,
                    new Rect(1920, 0, 3840, 1080)
                ),
            ];
            _contexts = [];
            return;
        }

        var displays = new List<DisplayInfo>();
        var contexts = new List<DisplayContext>();
        foreach (var monitor in Monitor.GetAll())
        {
            using (monitor)
            {
                if (monitor.TryGetDisplayInfo() is not { } info)
                    continue;
                displays.Add(info);
                if (monitor.TryCreateDeviceContext() is { } device)
                    contexts.Add(new DisplayContext(info, device));
            }
        }
        _contexts = contexts;
        _displays = displays;
        var duplicateIds = displays
            .GroupBy(d => d.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        if (duplicateIds.Count > 0)
        {
            _displays = displays
                .Select(d => duplicateIds.Contains(d.Id) ? d with { Id = d.ConnectionId } : d)
                .ToArray();
            _contexts = contexts
                .Select(c =>
                    duplicateIds.Contains(c.Info.Id)
                        ? c with
                        {
                            Info = c.Info with { Id = c.Info.ConnectionId },
                        }
                        : c
                )
                .ToArray();
        }
    }

    public void InvalidateGamma()
    {
        lock (_sync)
        {
            if (!_isDisposed && !_isUpdating)
                _lastInvalidation = _clock();
        }
    }

    public void InvalidateDeviceContexts()
    {
        lock (_sync)
        {
            if (_isDisposed || _isUpdating)
                return;
            _contextsValid = false;
            _lastInvalidation = _clock();
        }
    }

    internal void OnDisplayState(int state) => OnDisplayState(state, "display");

    private void OnDisplayState(int state, string source)
    {
        Diagnostics.ColorTrace.Write($"Display power [{source}]: {state}");
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _displayOff = state == 0;
            if (_displayOff)
            {
                _recoveryDeadline = 0;
                _handoverDeadline = 0;
                _recoveryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        if (state == 0)
        {
            SleepTransitionRequested?.Invoke();
            return;
        }
        Recover($"{source} power={state}");
    }

    public void Recover() => Recover("requested");

    private void Recover(string reason)
    {
        bool wasProtectedHandover;
        lock (_sync)
        {
            if (_isDisposed || _isUpdating || _isRecovering)
                return;
            _isRecovering = true;
            wasProtectedHandover = _protectedHandover;
            _displayOff = false;
            _contextsValid = false;
            _lastInvalidation = _clock();
        }

        try
        {
            Diagnostics.ColorTrace.Write($"Recovery [{reason}] started");
            // Recompute targets first. SetGamma queues them during this callback,
            // so the first recovery write is not immediately duplicated.
            RecoveryRequested?.Invoke();
            if (reason != "requested" || _protectedHandover)
                WakeTransitionRequested?.Invoke();
            lock (_sync)
            {
                if (_isDisposed || !_hasRequest || _displayOff)
                    return;
                var now = _clock();
                if (reason == "requested")
                    _handoverDeadline = 0;
                else if (
                    _settings.IsWakeRecoveryEnabled
                    && !_protectedHandover
                    && _recoveryDeadline <= now
                    && _nightLightActive()
                )
                {
                    // Let Windows finish restoring its warm fallback before
                    // handing the color layer back to per-monitor profiles.
                    _handoverDeadline = now + 1500;
                    Diagnostics.ColorTrace.Write("Night Light wake handover: wait 1500 ms");
                }
                if (!wasProtectedHandover && !_protectedHandover && now >= _handoverDeadline)
                    Apply(true, reason != "requested");
                if (_settings.IsWakeRecoveryEnabled)
                {
                    _recoveryDeadline = _clock() + 5000;
                    if (_runRecoveryTimer)
                        _recoveryTimer.Change(50, 50);
                }
            }
        }
        finally
        {
            lock (_sync)
                _isRecovering = false;
        }
        DisplaysChanged?.Invoke();
    }

    internal void RecoveryTick()
    {
        // Do not accumulate blocked callbacks when a driver takes a long time.
        if (!System.Threading.Monitor.TryEnter(_sync))
            return;
        try
        {
            if (_isDisposed)
                return;
            if (_displayOff || !_settings.IsWakeRecoveryEnabled || _clock() >= _recoveryDeadline)
            {
                _recoveryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }
            if (_protectedHandover || _clock() < _handoverDeadline)
                return;
            Apply(true);
        }
        finally
        {
            System.Threading.Monitor.Exit(_sync);
        }
    }

    internal void WatchdogTick()
    {
        // Independent of UI timers and foreground changes. Readback checks do
        // not rewrite matching LUTs, so an external reset can be repaired while
        // the application stays in the tray without fighting a stable filter.
        if (!System.Threading.Monitor.TryEnter(_sync))
            return;
        try
        {
            if (
                _isDisposed
                || !_hasRequest
                || _displayOff
                || _protectedHandover
                || _isUpdating
                || _isRecovering
            )
                return;
            var now = _clock();
            if (
                now < _handoverDeadline
                || (_settings.IsWakeRecoveryEnabled && now < _recoveryDeadline)
            )
                return;
            Apply(true, traceChecks: false);
        }
        finally
        {
            System.Threading.Monitor.Exit(_sync);
        }
    }

    public void SetGamma(
        ColorConfiguration fallback,
        IReadOnlyDictionary<string, ColorConfiguration>? configurations = null
    )
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            if (!_hasRequest && _runRecoveryTimer && !StartOptions.Current.IsPreview)
                _watchdogTimer.Change(250, 250);
            _hasRequest = true;
            _fallback = fallback;
            _requested = configurations ?? new Dictionary<string, ColorConfiguration>();
            if (
                !_displayOff
                && !_protectedHandover
                && !_isRecovering
                && _clock() >= _handoverDeadline
            )
                Apply(false);
        }
    }

    public void SetProtectedHandover(bool active)
    {
        lock (_sync)
        {
            _protectedHandover = active;
            _handoverDeadline = 0;
        }
    }

    public bool ApplyProtectedProfile()
    {
        lock (_sync)
        {
            if (_isDisposed || _displayOff || !_hasRequest)
                return false;
            _contextsValid = false;
            // A stored LUT may match even when scanout has not caught up.
            // Force a fresh write while the desktop is covered.
            Apply(true, false);
            return _contexts.Count > 0 && FailedDisplayIds.Count == 0;
        }
    }

    public void ReportProtectionFailure(string reason)
    {
        lock (_sync)
        {
            FailedDisplayIds = _displays.Select(display => display.Id).ToArray();
            FailureReasons = _displays.ToDictionary(
                display => display.Id,
                _ => "Proteção: " + reason + " Usa Reaplicar agora para tentar novamente."
            );
            ApplyStatusChanged?.Invoke();
        }
    }

    private void Apply(bool force, bool verifyBeforeWrite = true, bool traceChecks = true)
    {
        EnsureContexts();
        var now = _clock();
        var stale =
            force
            || (now - _lastInvalidation <= 2000 && now - _lastUpdate >= 200)
            || (_settings.IsGammaPollingEnabled && now - _lastUpdate >= 1000);
        var failures = _displays
            .Where(d => _contexts.All(c => c.Info.Id != d.Id))
            .Select(d => d.Id)
            .ToList();
        var reasons = failures.ToDictionary(id => id, _ => "Não foi possível aceder ao ecrã.");
        if (StartOptions.Current.IsPreview)
        {
            failures.Clear();
            reasons.Clear();
        }
        _isUpdating = true;
        var updated = false;
        var trace = new System.Text.StringBuilder();
        try
        {
            foreach (var context in _contexts)
            {
                var config = _requested.TryGetValue(context.Info.Id, out var value)
                    ? value
                    : _fallback;
                config = config.Clamp(500, 20000, 0.01, 1);
                trace.Append(
                    $"{context.Info.Name} ({context.Info.Id}): {config}; profile={_requested.ContainsKey(context.Info.Id)}; "
                );
                if (
                    !stale
                    && _applied.TryGetValue(context.Info.Id, out var previous)
                    && !GammaColor.RequiresUpdate(previous, config, 1, 0.001)
                )
                    continue;

                updated = true;
                var color = GammaColor.FromConfiguration(config);
                var started = Stopwatch.GetTimestamp();
                var applied = verifyBeforeWrite
                    ? context.Device.EnsureGamma(color.Red, color.Green, color.Blue)
                    : context.Device.SetGamma(color.Red, color.Green, color.Blue);
                if ((force && traceChecks) || context.Device.DidWriteGamma is true)
                    Diagnostics.ColorTrace.Write(
                        $"Color {(force ? "recovery" : "update")} [{context.Info.Name}]: {config}; {context.Device.ApplyDiagnostics}; write={context.Device.DidWriteGamma}; success={applied}; {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms"
                    );
                if (applied)
                    _applied[context.Info.Id] = config;
                else
                {
                    failures.Add(context.Info.Id);
                    reasons[context.Info.Id] =
                        context.Device.FailureReason ?? "O driver recusou os valores pedidos.";
                    _applied.Remove(context.Info.Id);
                }
            }
            if (updated)
                _lastUpdate = now;
            if (!FailedDisplayIds.SequenceEqual(failures) || !FailureReasons.SequenceEqual(reasons))
            {
                FailedDisplayIds = failures.ToArray();
                FailureReasons = reasons;
                ApplyStatusChanged?.Invoke();
            }
            var status = trace + $"failures={string.Join(";", reasons.Values)}";
            if (_lastTrace != status)
            {
                _lastTrace = status;
                Diagnostics.ColorTrace.Write(status);
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public void Dispose()
    {
        _events.Dispose();
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _recoveryTimer.Dispose();
            _watchdogTimer.Dispose();
            foreach (var context in _contexts)
            {
                context.Device.ResetGamma();
                context.Device.Dispose();
            }
        }
    }
}
