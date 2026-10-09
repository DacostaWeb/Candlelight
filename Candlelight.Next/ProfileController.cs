using Candlelight.Engine;

namespace Candlelight.Next;

/// <summary>Schedules and rendering keep running independently of the control window.</summary>
internal sealed class ProfileController : IDisposable
{
    private readonly ProfileStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _apply = new(1);
    private readonly Dictionary<string, (ColorProfile Profile, bool Enabled)> _last = new();
    private readonly Task _loop;
    public MagnificationEngine Engine { get; }
    public string? Error { get; private set; }

    public ProfileController(ProfileStore store, Action<string> log)
    {
        _store = store;
        Engine = new(log);
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
            foreach (var monitor in _store.Read().Monitors)
            {
                var target = (monitor.Evaluate(now), monitor.Enabled);
                if (!_last.TryGetValue(monitor.Id, out var last) || last != target)
                {
                    await Engine.ApplyAsync(monitor.Id, target.Item1, target.Enabled);
                    _last[monitor.Id] = target;
                }
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
