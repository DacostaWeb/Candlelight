using System;
using System.Collections.Generic;
using System.Linq;

namespace LightBulb.Core;

public sealed record TransitionCurve(
    double X1 = 0.42,
    double Y1 = 0,
    double X2 = 0.58,
    double Y2 = 1
)
{
    public static TransitionCurve Smooth { get; } = new();
    public static TransitionCurve Linear { get; } = new(0, 0, 1, 1);
    public static TransitionCurve Early { get; } = new(0.15, 0.65, 0.4, 1);
    public static TransitionCurve Late { get; } = new(0.6, 0, 0.85, 0.35);

    public TransitionCurve Normalize()
    {
        static double Unit(double value, double fallback) =>
            double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
        var x1 = Unit(X1, 0.42);
        var y1 = Unit(Y1, 0);
        return new(x1, y1, Math.Max(x1, Unit(X2, 0.58)), Math.Max(y1, Unit(Y2, 1)));
    }

    public double Evaluate(double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;
        var c = Normalize();
        static double Bezier(double t, double a, double b) =>
            3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        double lo = 0,
            hi = 1;
        for (var i = 0; i < 32; i++)
        {
            var mid = (lo + hi) / 2;
            if (Bezier(mid, c.X1, c.X2) < progress)
                lo = mid;
            else
                hi = mid;
        }
        return Bezier((lo + hi) / 2, c.Y1, c.Y2);
    }
}

public sealed record SolarCycleOptions
{
    public double SunriseMinutes { get; init; } = 120;
    public double SunsetMinutes { get; init; } = 120;
    public TransitionCurve SunriseCurve { get; init; } = TransitionCurve.Smooth;
    public TransitionCurve SunsetCurve { get; init; } = TransitionCurve.Smooth;

    public static double SafeDuration(double minutes) =>
        double.IsFinite(minutes) ? Math.Clamp(minutes, 0, 360) : 120;
}

public static class SolarCycle
{
    public static (DateTimeOffset Sunrise, DateTimeOffset Sunset) Events(
        DateOnly date,
        GeoLocation? location,
        SolarTimes fallback,
        TimeZoneInfo zone
    )
    {
        var noon = date.ToDateTime(new TimeOnly(12, 0));
        var times = location is { } geo
            ? SolarTimes.Calculate(geo, new DateTimeOffset(noon, zone.GetUtcOffset(noon)))
            : fallback;
        DateTimeOffset At(TimeOnly time)
        {
            var local = date.ToDateTime(time);
            // A manually selected time can fall in the spring DST gap.
            while (zone.IsInvalidTime(local))
                local = local.AddMinutes(1);
            return new(local, zone.GetUtcOffset(local));
        }
        return (At(times.Sunrise), At(times.Sunset));
    }

    public static ColorConfiguration Evaluate(
        SolarCycleOptions options,
        ColorConfiguration day,
        ColorConfiguration night,
        GeoLocation? location,
        SolarTimes fallback,
        DateTimeOffset instant,
        TimeZoneInfo? timeZone = null
    )
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        var events =
            new List<(
                DateTimeOffset Start,
                DateTimeOffset End,
                ColorConfiguration Target,
                TransitionCurve Curve
            )>();
        for (var offset = -2; offset <= 1; offset++)
        {
            var (rise, set) = Events(date.AddDays(offset), location, fallback, zone);
            events.Add(
                (
                    rise,
                    rise.AddMinutes(SolarCycleOptions.SafeDuration(options.SunriseMinutes)),
                    day,
                    options.SunriseCurve ?? TransitionCurve.Smooth
                )
            );
            events.Add(
                (
                    set.AddMinutes(-SolarCycleOptions.SafeDuration(options.SunsetMinutes)),
                    set,
                    night,
                    options.SunsetCurve ?? TransitionCurve.Smooth
                )
            );
        }
        var source = night;
        var target = night;
        var start = DateTimeOffset.MinValue;
        var end = start;
        var curve = TransitionCurve.Linear;
        ColorConfiguration At(DateTimeOffset time)
        {
            if (time >= end)
                return target;
            if (time <= start)
                return source;
            return Mix(source, target, curve.Evaluate((time - start) / (end - start)));
        }
        foreach (var item in events.OrderBy(e => e.Start).ThenBy(e => e.End))
        {
            if (item.Start > instant)
                break;
            // If long transitions overlap, continue from the current color instead of jumping.
            source = At(item.Start);
            target = item.Target;
            start = item.Start;
            end = item.End;
            curve = item.Curve;
        }
        return At(instant);
    }

    public static ColorConfiguration Mix(
        ColorConfiguration from,
        ColorConfiguration to,
        double amount
    ) =>
        new(
            from.Temperature + (to.Temperature - from.Temperature) * amount,
            from.Brightness + (to.Brightness - from.Brightness) * amount
        );
}
