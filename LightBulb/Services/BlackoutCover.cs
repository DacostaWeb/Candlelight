using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace LightBulb.Services;

internal interface IBlackoutCover : IDisposable
{
    void Prepare();
    Task ShowAsync(CancellationToken cancellationToken);
    void Hide();
    Task HideAsync()
    {
        Hide();
        return Task.CompletedTask;
    }
}

internal sealed class BlackoutCover : IBlackoutCover
{
    private readonly List<Window> _windows = [];
    private volatile bool _disposed;
    private volatile bool _requested;

    internal static Window CreateWindow() =>
        new()
        {
            Title = "Candlelight · proteção ao acordar",
            Background = Brushes.Black,
            Content = new Border { Background = Brushes.Black },
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            TransparencyLevelHint = [WindowTransparencyLevel.None],
        };

    public void Prepare()
    {
        _requested = true;
        if (Dispatcher.UIThread.CheckAccess())
            TryPrepare();
        else
            Dispatcher.UIThread.Post(TryPrepare, DispatcherPriority.Send);
    }

    private void TryPrepare()
    {
        try
        {
            if (_requested)
                Show();
        }
        catch (Exception error)
        {
            Diagnostics.ColorTrace.Write($"Wake cover preparation failed: {error.Message}");
        }
    }

    private void Show()
    {
        if (_disposed || _windows.Count > 0)
            return;
        var first = CreateWindow();
        try
        {
            foreach (var screen in first.Screens.All)
            {
                var window = _windows.Count == 0 ? first : CreateWindow();
                _windows.Add(window);
                window.Position = screen.Bounds.Position;
                window.Width = screen.Bounds.Width / screen.Scaling;
                window.Height = screen.Bounds.Height / screen.Scaling;
                window.Show();
                // Reassert after the initial native DPI/position negotiation.
                window.Position = screen.Bounds.Position;
                window.Width = screen.Bounds.Width / screen.Scaling;
                window.Height = screen.Bounds.Height / screen.Scaling;
            }
            if (_windows.Count == 0)
                throw new InvalidOperationException("Não foram encontrados ecrãs para a proteção.");
        }
        catch
        {
            first.Close();
            Hide();
            throw;
        }
    }

    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        _requested = true;
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    Show();
            },
            DispatcherPriority.Send
        );
        // Allow the compositor to paint the opaque surfaces before changing
        // either filter. The cover is already prepared while the display is off.
        await Task.Delay(250, cancellationToken);
    }

    public void Hide()
    {
        _requested = false;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Hide, DispatcherPriority.Send);
            return;
        }
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    public async Task HideAsync() =>
        await Dispatcher.UIThread.InvokeAsync(Hide, DispatcherPriority.Send);

    public void Dispose()
    {
        _disposed = true;
        Hide();
    }
}
