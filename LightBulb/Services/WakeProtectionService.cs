using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LightBulb.PlatformInterop;

namespace LightBulb.Services;

internal interface INightLightControl
{
    bool? ReadActive();
    bool SetActive(bool active);
}

internal sealed class NightLightControl : INightLightControl
{
    public bool? ReadActive() => NightLight.ReadActive();

    public bool SetActive(bool active) => NightLight.TrySetActive(active);
}

public sealed class WakeProtectionService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly GammaService _gamma;
    private readonly INightLightControl _nightLight;
    private readonly IBlackoutCover _cover;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly string? _statePath;
    private readonly Action _emergencyExit;
    private readonly Func<long> _clock;
    private readonly System.Threading.Timer? _failSafe;
    private CancellationTokenSource? _transitionCancellation;
    private bool? _originalActive;
    private bool _ownsState;
    private bool _initialized;
    private volatile bool _disposed;
    private volatile bool _sleeping;
    private bool _locked;
    private volatile bool _transitionActive;
    private long _transitionStarted;
    private int _generation;

    public WakeProtectionService(SettingsService settings, GammaService gamma)
        : this(
            settings,
            gamma,
            new NightLightControl(),
            new BlackoutCover(),
            Task.Delay,
            Path.Combine(
                Path.GetDirectoryName(StartOptions.Current.SettingsPath)!,
                "NightLightGuard.state"
            ),
            () => Environment.Exit(3),
            true
        ) { }

    internal WakeProtectionService(
        SettingsService settings,
        GammaService gamma,
        INightLightControl nightLight,
        IBlackoutCover cover,
        Func<TimeSpan, CancellationToken, Task> delay,
        string? statePath = null,
        Action? emergencyExit = null,
        bool runFailSafe = false,
        Func<long>? clock = null
    )
    {
        _settings = settings;
        _gamma = gamma;
        _nightLight = nightLight;
        _cover = cover;
        _delay = delay;
        _statePath = statePath;
        _emergencyExit = emergencyExit ?? (() => { });
        _clock = clock ?? (() => Environment.TickCount64);
        _gamma.SleepTransitionRequested += PrepareForSleep;
        _gamma.WakeTransitionRequested += OnWakeRequested;
        _gamma.SessionLockChanged += OnSessionLockChanged;
        if (runFailSafe && !StartOptions.Current.IsPreview)
            _failSafe = new(_ => CheckFailSafe(), null, 1000, 1000);
    }

    private bool Enabled =>
        _settings.IsWakeRecoveryEnabled && _settings.IsNightLightProtectionEnabled;

    public void Initialize()
    {
        if (_initialized || StartOptions.Current.IsPreview)
            return;
        _initialized = true;
        _originalActive = _nightLight.ReadActive();
        try
        {
            if (_statePath is not null && File.Exists(_statePath))
            {
                var saved = File.ReadAllText(_statePath).Trim();
                if (saved is "on" or "off")
                {
                    _originalActive = saved == "on";
                    _ownsState = true;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Diagnostics.ColorTrace.Write(
                $"Wake protection state could not be read: {error.Message}"
            );
        }
        if ((Enabled || _ownsState) && _nightLight.ReadActive() is true)
            OnWakeRequested();
    }

    private bool TakeOwnership()
    {
        if (_ownsState)
            return true;
        if (_originalActive is null)
            return false;
        try
        {
            if (_statePath is not null)
            {
                var temporary = _statePath + ".tmp";
                File.WriteAllText(temporary, _originalActive.Value ? "on" : "off");
                File.Move(temporary, _statePath, true);
            }
            _ownsState = true;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Diagnostics.ColorTrace.Write(
                $"Wake protection could not save original Night Light state: {error.Message}"
            );
            return false;
        }
    }

    internal void PrepareForSleep()
    {
        if (!_initialized || _disposed || !Enabled || _sleeping)
            return;
        _sleeping = true;
        _generation++;
        _transitionCancellation?.Cancel();
        _transitionActive = false;
        _gamma.SetProtectedHandover(true);
        _cover.Prepare();
        // Synchronous: Modern Standby may pause desktop code as soon as this
        // display-off notification returns. Do not defer arming to a UI timer.
        var armed = TakeOwnership() && _nightLight.SetActive(true);
        Diagnostics.ColorTrace.Write($"Wake protection prepared: Night Light armed={armed}");
    }

    internal void OnSessionLockChanged(bool locked)
    {
        _locked = locked;
        if (locked)
        {
            if (Enabled)
                PrepareForSleep();
            _generation++;
            _transitionCancellation?.Cancel();
            _transitionActive = false;
            if (_ownsState)
                _nightLight.SetActive(true);
        }
    }

    internal Task PendingTransition { get; private set; } = Task.CompletedTask;

    private void OnWakeRequested()
    {
        if (!_transitionActive)
            PendingTransition = TransitionAsync();
    }

    internal async Task TransitionAsync()
    {
        if (
            !_initialized
            || _disposed
            || _locked
            || _transitionActive
            || (!Enabled && !_sleeping && !(_ownsState && _nightLight.ReadActive() is true))
        )
            return;
        _sleeping = false;
        _transitionActive = true;
        _transitionStarted = _clock();
        var generation = ++_generation;
        _transitionCancellation?.Dispose();
        _transitionCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancellationToken = _transitionCancellation.Token;
        var success = false;
        _gamma.SetProtectedHandover(true);
        try
        {
            Diagnostics.ColorTrace.Write("Protected wake: covering displays");
            await _cover.ShowAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var nightLightActive = _nightLight.ReadActive();
            if (nightLightActive is null)
                throw new InvalidOperationException(
                    "O estado da Luz noturna não é suportado nesta versão do Windows."
                );
            if (nightLightActive is true)
            {
                if (!TakeOwnership() || !_nightLight.SetActive(false))
                    throw new InvalidOperationException(
                        "Não foi possível desligar a Luz noturna durante a passagem."
                    );
            }
            Diagnostics.ColorTrace.Write(
                "Protected wake: Night Light off; waiting for Windows transition"
            );
            await _delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            if (!_gamma.ApplyProtectedProfile())
                throw new InvalidOperationException(
                    "O Windows não confirmou o perfil durante a proteção."
                );
            await _delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_gamma.ApplyProtectedProfile())
                throw new InvalidOperationException(
                    "O perfil não ficou estável durante a proteção."
                );
            Diagnostics.ColorTrace.Write("Protected wake: profiles applied; uncovering displays");
            success = true;
        }
        catch (Exception error)
        {
            if (generation != _generation || _disposed)
                return;
            Diagnostics.ColorTrace.Write($"Protected wake fallback: {error.Message}");
            if (_ownsState)
                _nightLight.SetActive(true);
            _gamma.ReportProtectionFailure(error.Message);
            // Keep the cover while Windows restores the warm fallback.
            await _delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
        }
        finally
        {
            // A new sleep or session lock owns the existing cover now.
            if (generation == _generation && !_disposed)
            {
                await _cover.HideAsync();
                _gamma.SetProtectedHandover(!success);
                _transitionActive = false;
            }
        }
    }

    internal void CheckFailSafe()
    {
        if (_disposed || _sleeping || !_transitionActive || _clock() - _transitionStarted < 8000)
            return;
        // A stuck UI cannot process Close(). Terminating only this application
        // lets Windows remove its covers. Leave a warm fallback and a recovery
        // token before exiting; the next start restores the original state.
        if (_ownsState)
            _nightLight.SetActive(true);
        _emergencyExit();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _generation++;
        _gamma.SleepTransitionRequested -= PrepareForSleep;
        _gamma.WakeTransitionRequested -= OnWakeRequested;
        _gamma.SessionLockChanged -= OnSessionLockChanged;
        _transitionCancellation?.Cancel();
        _failSafe?.Dispose();
        if (_ownsState && _originalActive is { } original && _nightLight.SetActive(original))
        {
            if (_statePath is not null)
            {
                try
                {
                    File.Delete(_statePath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Diagnostics.ColorTrace.Write(
                        $"Wake protection state cleanup failed: {error.Message}"
                    );
                }
            }
        }
        _cover.Dispose();
        _transitionCancellation?.Dispose();
    }
}
