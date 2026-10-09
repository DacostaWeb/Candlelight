using System;
using System.Collections.Generic;
using System.Linq;

namespace LightBulb.Core;

public static class DailySchedule
{
    // Each time marks the START of a transition. Zero duration is an immediate change.
    // Transitions are capped at the next point, so overlapping transitions remain continuous.
    public static ColorConfiguration Evaluate(
        IReadOnlyList<SchedulePoint> points,
        TimeOnly time,
        ColorConfiguration fallback
    )
    {
        if (points.Count == 0)
            return fallback;

        var ordered = points
            .GroupBy(p => p.Time)
            .Select(g => g.Last())
            .OrderBy(p => p.Time)
            .ToArray();
        if (ordered.Length == 1)
            return ordered[0].Configuration;

        var index = Array.FindLastIndex(ordered, p => p.Time <= time);
        if (index < 0)
            index = ordered.Length - 1;

        var current = ordered[index];
        var previous = ordered[(index + ordered.Length - 1) % ordered.Length];
        var next = ordered[(index + 1) % ordered.Length];
        var elapsed = ForwardDistance(current.Time, time);
        var duration = Math.Min(
            Math.Max(0, current.Transition.TotalSeconds),
            ForwardDistance(current.Time, next.Time)
        );
        var fraction = duration <= 0 ? 1 : Math.Clamp(elapsed / duration, 0, 1);

        if (fraction >= 1)
            return current.Configuration;
        if (fraction <= 0)
            return previous.Configuration;

        return new ColorConfiguration(
            previous.Configuration.Temperature
                + (current.Configuration.Temperature - previous.Configuration.Temperature)
                    * fraction,
            previous.Configuration.Brightness
                + (current.Configuration.Brightness - previous.Configuration.Brightness) * fraction
        );
    }

    private static double ForwardDistance(TimeOnly from, TimeOnly to) =>
        (to.ToTimeSpan().TotalSeconds - from.ToTimeSpan().TotalSeconds + 86400) % 86400;
}
