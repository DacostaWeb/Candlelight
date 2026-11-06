using System;
using System.Linq;

namespace LightBulb.PlatformInterop.Internal;

// Night Light can overwrite a successfully verified ramp throughout its fade.
// Wait for a quiet interval instead of taking the same color layer back on
// every solar/UI tick. This state survives device-context recreation.
internal sealed class GammaRampGuard(Func<long>? clock = null)
{
    private sealed record Snapshot(GammaRamp Gdi, GammaRamp? Color)
    {
        public bool Matches(Snapshot other) =>
            Gdi.Matches(other.Gdi)
            && (
                Color is { } color
                    ? other.Color is { } otherColor && color.Matches(otherColor)
                    : other.Color is null
            );

        public bool EqualsExactly(Snapshot other) =>
            Exact(Gdi, other.Gdi)
            && (
                Color is { } color
                    ? other.Color is { } otherColor && Exact(color, otherColor)
                    : other.Color is null
            );

        private static bool Exact(GammaRamp left, GammaRamp right) =>
            left.Red.SequenceEqual(right.Red)
            && left.Green.SequenceEqual(right.Green)
            && left.Blue.SequenceEqual(right.Blue);
    }

    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private Snapshot? _owned;
    private Snapshot? _external;
    private long _lastExternalChange;

    public bool IsDeferred => _external is not null;

    public bool ShouldDefer(GammaRamp gdi, GammaRamp? color)
    {
        var current = new Snapshot(gdi, color);
        if (_external is null)
        {
            if (_owned is null || _owned.Matches(current))
                return false;
            _external = current;
            _lastExternalChange = _clock();
            return true;
        }

        // Compare successive observations exactly. A slow fade can move less
        // than the normal driver-quantization tolerance on each UI tick.
        if (!_external.EqualsExactly(current))
        {
            _external = current;
            _lastExternalChange = _clock();
        }
        if (_clock() - _lastExternalChange < 3000)
            return true;
        _external = null;
        return false;
    }

    public void Remember(GammaRamp gdi, GammaRamp? color)
    {
        _owned = new(gdi, color);
        _external = null;
    }

    public void Clear()
    {
        _owned = null;
        _external = null;
    }
}
