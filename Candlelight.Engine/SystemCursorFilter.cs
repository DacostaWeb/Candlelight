using System.Diagnostics;
using System.Text.Json;

namespace Candlelight.Engine;

/// <summary>Temporary runtime copies of standard cursor shapes; no scheme/registry writes.</summary>
internal sealed class SystemCursorFilter : IDisposable
{
    internal static readonly uint[] Ids =
    [
        32512,
        32513,
        32514,
        32515,
        32516,
        32642,
        32643,
        32644,
        32645,
        32646,
        32648,
        32649,
        32650,
    ];

    private sealed record Original(nint Handle, CursorImage Image);

    private sealed record SavedCursor(string Fingerprint, CursorImage Original);

    private sealed record Lease(
        int ProcessId,
        long StartTicks,
        Dictionary<uint, SavedCursor> Images
    );

    private readonly Dictionary<uint, Original> _originals = new();
    private readonly Dictionary<uint, string> _owned = new();
    private readonly string _leasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Candlelight.Next",
        "SystemCursorLease.json"
    );
    private readonly Action<string>? _log;
    private ChannelGain? _gain;
    private bool _recovered;
    public bool Active => _gain is not null && _owned.Count == Ids.Length;

    public SystemCursorFilter(Action<string>? log) => _log = log;

    public void Apply(ChannelGain gain)
    {
        if (_gain == gain)
            return;
        EnsureRecovered();
        if (_gain is null && _owned.Count > 0)
            Restore();
        if (
            _owned.Any(p =>
                CursorImage.Read(CursorNative.LoadCursor(0, (nint)p.Key)).Fingerprint != p.Value
            )
        )
            Restore();
        try
        {
            if (_originals.Count == 0)
                foreach (var id in Ids)
                {
                    var copy = CursorNative.CopyIcon(CursorNative.LoadCursor(0, (nint)id));
                    if (copy == 0)
                        throw MagnificationEngine.Error("Copy original cursor");
                    try
                    {
                        _originals.Add(id, new(copy, CursorImage.Read(copy)));
                    }
                    catch
                    {
                        CursorNative.DestroyCursor(copy);
                        throw;
                    }
                }
            foreach (var (id, original) in _originals)
            {
                var cursor = original.Image.Create(gain);
                if (!CursorNative.SetSystemCursor(cursor, id))
                {
                    CursorNative.DestroyCursor(cursor);
                    throw MagnificationEngine.Error("Apply colored cursor");
                }
                _owned[id] = CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint;
                SaveLease();
            }
            _gain = gain;
        }
        catch
        {
            try
            {
                Restore();
            }
            catch (Exception error)
            {
                _log?.Invoke("Cursor rollback failed: " + error);
            }
            throw;
        }
    }

    public void Refresh()
    {
        if (_gain is not { } gain)
            return;
        if (
            _owned.All(p =>
                CursorImage.Read(CursorNative.LoadCursor(0, (nint)p.Key)).Fingerprint == p.Value
            )
        )
            return;
        // Rebase on a changed Windows cursor theme, preserving the user's new shapes.
        Restore();
        Apply(gain);
    }

    public void Restore()
    {
        EnsureRecovered();
        if (_gain is null && _owned.Count == 0 && _originals.Count == 0)
            return;
        List<Exception> failures = [];
        foreach (var (id, fingerprint) in _owned.ToArray())
        {
            try
            {
                if (
                    CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint
                    == fingerprint
                )
                {
                    var copy = CursorNative.CopyIcon(_originals[id].Handle);
                    if (copy == 0)
                        throw MagnificationEngine.Error("Copy cursor for restoration");
                    if (!CursorNative.SetSystemCursor(copy, id))
                    {
                        CursorNative.DestroyCursor(copy);
                        throw MagnificationEngine.Error("Restore original cursor");
                    }
                }
                _owned.Remove(id);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        foreach (var (id, original) in _originals.ToArray())
            if (!_owned.ContainsKey(id))
            {
                CursorNative.DestroyCursor(original.Handle);
                _originals.Remove(id);
            }
        _gain = null;
        if (_owned.Count > 0)
            SaveLease();
        else if (File.Exists(_leasePath))
            File.Delete(_leasePath);
        if (failures.Count > 0)
            throw new AggregateException("Cursor restoration failed.", failures);
    }

    private void SaveLease()
    {
        using var process = Process.GetCurrentProcess();
        Directory.CreateDirectory(Path.GetDirectoryName(_leasePath)!);
        var images = _owned.ToDictionary(
            p => p.Key,
            p => new SavedCursor(p.Value, _originals[p.Key].Image)
        );
        File.WriteAllText(
            _leasePath + ".tmp",
            JsonSerializer.Serialize(
                new Lease(process.Id, process.StartTime.ToUniversalTime().Ticks, images)
            )
        );
        File.Move(_leasePath + ".tmp", _leasePath, true);
    }

    private void EnsureRecovered()
    {
        if (_recovered)
            return;
        Recover();
        _recovered = true;
    }

    private void Recover()
    {
        if (!File.Exists(_leasePath))
            return;
        var lease =
            JsonSerializer.Deserialize<Lease>(File.ReadAllText(_leasePath))
            ?? throw new InvalidDataException("Invalid cursor recovery state.");
        try
        {
            using var process = Process.GetProcessById(lease.ProcessId);
            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == lease.StartTicks)
                throw new InvalidOperationException(
                    "A running application already owns the colored cursors."
                );
        }
        catch (ArgumentException) { }
        // Restore only owned shapes. A newly selected user theme takes precedence.
        foreach (var (id, saved) in lease.Images)
        {
            if (!Ids.Contains(id))
                throw new InvalidDataException("Invalid cursor recovery ID.");
            if (
                CursorImage.Read(CursorNative.LoadCursor(0, (nint)id)).Fingerprint
                != saved.Fingerprint
            )
                continue;
            var cursor = saved.Original.Create(new(1, 1, 1));
            if (!CursorNative.SetSystemCursor(cursor, id))
            {
                CursorNative.DestroyCursor(cursor);
                throw MagnificationEngine.Error("Recover cursor after interrupted exit");
            }
        }
        _log?.Invoke("Recovered owned cursor shapes after interrupted exit.");
        File.Delete(_leasePath);
    }

    public void Dispose()
    {
        try
        {
            Restore();
        }
        catch (Exception error)
        {
            _log?.Invoke("Cursor exit restoration failed; recovery retained: " + error);
        }
        foreach (var original in _originals.Values)
            CursorNative.DestroyCursor(original.Handle);
        _originals.Clear();
        _owned.Clear();
    }
}
