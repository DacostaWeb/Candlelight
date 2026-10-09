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

    private readonly SettingsService _settings;
    private readonly object _sync = new();
    private readonly IDisposable _events;
    private readonly System.Threading.Timer _recoveryTimer;
    private IReadOnlyList<DisplayContext> _contexts = [];
    private IReadOnlyList<DisplayInfo> _displays = [];
    private bool _contextsValid;
    private bool _isUpdating;
    private bool _isDisposed;
    private bool _displayOff;
    private bool _hasRequest;
    private long _recoveryDeadline;
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
        bool registerEvents
    )
    {
        _settings = settings;
        _enumerateOverride = enumerate;
        _clock = clock;
        _lastInvalidation = clock() - 5000;
        _runRecoveryTimer = registerEvents;
        _recoveryTimer = new System.Threading.Timer(
            _ => RecoveryTick(),
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
                    OnDisplayState
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegisterDisplayState(
                    PowerSettingNotification.Ids.SessionDisplayStatusChanged,
                    OnDisplayState
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegisterDisplayState(
                    PowerSettingNotification.Ids.MonitorPowerStateChanged,
                    OnDisplayState
                ) ?? Disposable.Null,
                PowerSettingNotification.TryRegister(
                    PowerSettingNotification.Ids.PowerSavingStatusChanged,
                    InvalidateGamma
                ) ?? Disposable.Null,
                ResumeNotification.Register(Recover),
                SystemEvent.Register(SystemEvent.Ids.DisplayChanged, Recover),
                SystemEvent.Register(SystemEvent.Ids.PaletteChanged, InvalidateDeviceContexts),
                SystemEvent.Register(SystemEvent.Ids.SettingsChanged, InvalidateDeviceContexts),
                SystemEvent.Register(SystemEvent.Ids.SystemColorsChanged, InvalidateDeviceContexts)
            );
    }

    // Runs on the window's message thread, before cached values are reapplied.
    public event Action? RecoveryRequested;
    public event Action? DisplaysChanged;
    public event Action? ApplyStatusChanged;
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

    internal void OnDisplayState(int state)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _displayOff = state == 0;
            if (_displayOff)
            {
                _recoveryDeadline = 0;
                _recoveryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }
        }
        Recover();
    }

    public void Recover()
    {
        lock (_sync)
        {
            if (_isDisposed || _isUpdating)
                return;
            _displayOff = false;
            _contextsValid = false;
            _lastInvalidation = _clock();
        }

        // Re-evaluate the current time after sleep. Never fade from daylight on wake.
        RecoveryRequested?.Invoke();
        lock (_sync)
        {
            if (_isDisposed || !_hasRequest)
                return;
            Apply(true);
            if (_settings.IsWakeRecoveryEnabled)
            {
                _recoveryDeadline = _clock() + 5000;
                if (_runRecoveryTimer)
                    _recoveryTimer.Change(50, 50);
            }
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
            Apply(true);
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
            _hasRequest = true;
            _fallback = fallback;
            _requested = configurations ?? new Dictionary<string, ColorConfiguration>();
            if (!_displayOff)
                Apply(false);
        }
    }

    private void Apply(bool force)
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
                if (context.Device.SetGamma(color.Red, color.Green, color.Blue))
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
            foreach (var context in _contexts)
            {
                context.Device.ResetGamma();
                context.Device.Dispose();
            }
        }
    }
}
