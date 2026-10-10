using System.Runtime.InteropServices;
using Candlelight.Engine;

namespace Candlelight.Next;

/// <summary>A bounded, non-activating black cover; no controls or input injection.</summary>
internal sealed class SecureStartupCover : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private nint _window;
    private int _closing;

    internal SecureStartupCover()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Candlelight secure startup cover",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    internal Task Ready => _ready.Task;
    internal nint[] Windows =>
        Volatile.Read(ref _window) is var window && window != 0 ? [window] : [];

    private void Run()
    {
        var name = "Candlelight.SecureStartup." + Guid.NewGuid().ToString("N");
        Native.WndProc procedure = (window, message, wParam, lParam) =>
        {
            if (message is 0x0010 or 0x0113) // WM_CLOSE / WM_TIMER: timeout always releases the cover.
            {
                Native.PostQuitMessage(0);
                return 0;
            }
            if (message == 0x0021)
                return 3; // MA_NOACTIVATE
            if (message == 0x0084)
                return -1; // HTTRANSPARENT
            return Native.DefWindowProc(window, message, wParam, lParam);
        };
        var registered = false;
        nint host = 0;
        try
        {
            var windowClass = new Native.WindowClass
            {
                Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
                Procedure = procedure,
                Instance = Native.GetModuleHandle(null),
                Background = Native.GetStockObject(4), // BLACK_BRUSH
                Name = name,
            };
            registered = Native.RegisterClassEx(ref windowClass) != 0;
            if (!registered)
                throw MagnificationEngine.Error("Register startup cover");
            host = Native.CreateWindowEx(
                0x080800A8,
                name,
                null,
                0x80000000,
                GetSystemMetrics(76),
                GetSystemMetrics(77),
                GetSystemMetrics(78),
                GetSystemMetrics(79),
                0,
                0,
                windowClass.Instance,
                0
            );
            if (host == 0 || Native.SetTimer(host, 1, 2000, 0) == 0)
                throw MagnificationEngine.Error("Create startup cover");
            if (!Native.SetLayeredWindowAttributes(host, 0, 255, 2))
                throw MagnificationEngine.Error("Set startup cover opacity");
            Volatile.Write(ref _window, host);
            if (Volatile.Read(ref _closing) == 0)
            {
                // Explicit SHOWWINDOW also overrides an inherited SW_HIDE startup hint.
                Native.SetWindowPos(host, -1, 0, 0, 0, 0, 0x53);
                Native.InvalidateRect(host, 0, true);
                Native.UpdateWindow(host);
                Native.DwmFlush();
            }
            _ready.TrySetResult();
            while (
                Volatile.Read(ref _closing) == 0 && Native.GetMessage(out var message, 0, 0, 0) > 0
            )
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
        }
        finally
        {
            Volatile.Write(ref _window, 0);
            if (host != 0)
            {
                Native.KillTimer(host, 1);
                Native.DestroyWindow(host);
            }
            if (registered)
                Native.UnregisterClass(name, Native.GetModuleHandle(null));
            GC.KeepAlive(procedure);
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closing, 1);
        if (Volatile.Read(ref _window) is var window && window != 0)
            Native.PostMessage(window, 0x0010, 0, 0);
        _thread.Join(1000);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
