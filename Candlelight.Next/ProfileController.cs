using Candlelight.Engine;

namespace Candlelight.Next;

/// <summary>Schedules and rendering keep running independently of the control window.</summary>
internal sealed class ProfileController : IDisposable
{
    private readonly ProfileStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _apply = new(1);
    private readonly Dictionary<string, (ColorProfile Profile, bool Enabled)> _last = new();
    private bool? _lastCursorFiltering;
    private byte[]? _lastSecureProfiles;
    private long _lastSecurePublish;
    private readonly Task _loop;
    private readonly Action<string> _log;
    public MagnificationEngine Engine { get; }
    public string? Error { get; private set; }
    public string? SecureDesktopError { get; private set; }

    public ProfileController(ProfileStore store, Action<string> log)
    {
        _store = store;
        _log = log;
        Engine = new(log, trackInputDesktop: true, blackInactiveLocalSurfaces: true);
        _loop = Task.Run(async () =>
        {
            try
            {
                await Engine.Ready;
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                do
                {
                    try
                    {
                        await ApplyNowAsync();
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Error = error.Message;
                        log(error.ToString());
                    }
                } while (await timer.WaitForNextTickAsync(_stop.Token));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception error)
            {
                Error = error.Message;
                log(error.ToString());
            }
        });
    }

    public async Task ApplyNowAsync()
    {
        await _apply.WaitAsync(_stop.Token);
        try
        {
            _store.EnsureDisplays(DisplayCatalog.GetDisplays());
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var settings = _store.Read();
            try
            {
                var secure = SecureDesktopProfiles.Encode(
                    settings.Monitors.Select(m => new SecureMonitorProfile(
                        m.Id,
                        m.Enabled,
                        m.Manual,
                        m.Scheduled ? m.Schedule.ToArray() : []
                    ))
                );
                if (
                    _lastSecureProfiles is null
                    || !secure.AsSpan().SequenceEqual(_lastSecureProfiles)
                    || Environment.TickCount64 - _lastSecurePublish > 10000
                )
                {
                    SecureDesktopProfiles.Publish(secure);
                    _lastSecureProfiles = secure;
                    _lastSecurePublish = Environment.TickCount64;
                }
                SecureDesktopError = null;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (SecureDesktopError != error.Message)
                    _log("Secure profile transfer failed: " + error.Message);
                SecureDesktopError = error.Message;
            }
            if (_lastCursorFiltering != settings.FilterCursor)
            {
                await Engine.SetCursorFilteringAsync(settings.FilterCursor);
                _lastCursorFiltering = settings.FilterCursor;
            }
            var changes = settings
                .Monitors.Select(monitor =>
                {
                    var target = (monitor.Evaluate(now), monitor.Enabled);
                    return (DisplayId: monitor.Id, Profile: target.Item1, target.Enabled);
                })
                .Where(p =>
                    !_last.TryGetValue(p.DisplayId, out var last) || last != (p.Profile, p.Enabled)
                )
                .ToArray();
            if (changes.Length > 0)
            {
                await Engine.ApplyProfilesAsync(changes);
                foreach (var p in changes)
                    _last[p.DisplayId] = (p.Profile, p.Enabled);
            }
            Error = null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Error = error.Message;
            throw;
        }
        finally
        {
            _apply.Release();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException) { }
        Engine.Dispose();
    }
}
