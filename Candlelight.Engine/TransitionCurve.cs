namespace Candlelight.Engine;

/// <summary>Monotone cubic Bézier easing, with time on x and progress on y.</summary>
public sealed record TransitionCurve(double X1, double Y1, double X2, double Y2)
{
    public static TransitionCurve Linear { get; } = new(1d / 3, 1d / 3, 2d / 3, 2d / 3);
    public static TransitionCurve Smooth { get; } = new(1d / 3, 0, 2d / 3, 1);
    public static TransitionCurve SlowStart { get; } = new(.65, 0, .85, .45);
    public static TransitionCurve SlowEnd { get; } = new(.15, .55, .35, 1);

    public void Validate()
    {
        if (
            new[] { X1, Y1, X2, Y2 }.Any(v => !double.IsFinite(v) || v < 0 || v > 1)
            || X1 > X2
            || Y1 > Y2
        )
            throw new ArgumentException(
                "Os controlos da curva devem avançar de 0 a 100%, sem inverter a transição."
            );
    }

    public double Evaluate(double time)
    {
        if (time <= 0)
            return 0;
        if (time >= 1)
            return 1;
        // Invert x(t). Bisection also handles vertical tangents at either endpoint.
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 32; i++)
        {
            var t = (low + high) / 2;
            if (Coordinate(t, X1, X2) < time)
                low = t;
            else
                high = t;
        }
        return Coordinate((low + high) / 2, Y1, Y2);
    }

    private static double Coordinate(double t, double a, double b) =>
        3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
}
