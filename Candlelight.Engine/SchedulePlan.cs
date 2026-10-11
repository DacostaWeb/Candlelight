namespace Candlelight.Engine;

public enum TimeAnchor
{
    Clock,
    Sunrise,
    Sunset,
}

public sealed record TimingPoint(
    string Id,
    TimeOnly Time,
    TimeAnchor Anchor = TimeAnchor.Clock,
    int OffsetMinutes = 0,
    int? TransitionMinutes = null,
    TransitionCurve? Curve = null
);

public sealed record SchedulePlan
{
    public int TransitionMinutes { get; set; } = 15;
    public bool SmoothTransitions { get; set; } = true;
    public SolarLocation? Location { get; set; }
    public List<TimingPoint> Points { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeZoneInfo TimeZone =>
        Location is null
            ? TimeZoneInfo.Local
            : TimeZoneInfo.FindSystemTimeZoneById(Location.TimeZoneId);

    public void Validate()
    {
        if (TransitionMinutes is < 0 or > 1440)
            throw new ArgumentException("A transição deve ter entre 0 e 1440 minutos.");
        Location?.Validate();
        ValidatePoints(Points);
    }

    public static void ValidatePoints(IReadOnlyList<TimingPoint> points)
    {
        if (points.Count > 48 || points.Select(p => p.Id).Distinct().Count() != points.Count)
            throw new ArgumentException("Um horário pode ter até 48 pontos diferentes.");
        if (
            points.Any(p =>
                string.IsNullOrWhiteSpace(p.Id)
                || p.Id.Length > 80
                || !Enum.IsDefined(p.Anchor)
                || p.OffsetMinutes is < -720 or > 720
            )
        )
            throw new ArgumentException("Ponto do horário inválido.");
        if (
            points
                .Select(p =>
                    (p.Anchor, p.Anchor == TimeAnchor.Clock ? p.Time.Ticks : p.OffsetMinutes)
                )
                .Distinct()
                .Count() != points.Count
        )
            throw new ArgumentException("Já existe um ponto nessa hora.");
        foreach (var point in points)
        {
            if (point.TransitionMinutes is < 0 or > 1440)
                throw new ArgumentException("A transição deve ter entre 0 e 1440 minutos.");
            point.Curve?.Validate();
        }
    }
}

public sealed record ResolvedTiming(TimingPoint Point, DateTimeOffset Time, bool UsedFallback);

public sealed record ScheduledColorEvent(
    DateTimeOffset Time,
    ColorProfile Profile,
    int TransitionMinutes,
    bool Smooth,
    TransitionCurve? Curve = null
);

public static class ScheduleTimeline
{
    public static IReadOnlyList<TimingPoint> Points(SchedulePlan plan, MonitorProfile monitor) =>
        monitor.UseSharedSchedule ? plan.Points : monitor.OwnTiming;

    public static ResolvedTiming[] ResolveDay(
        SchedulePlan plan,
        MonitorProfile monitor,
        DateOnly date
    )
    {
        var zone = plan.TimeZone;
        var solar = plan.Location is null ? null : SolarTimes.Calculate(date, plan.Location);
        return Points(plan, monitor)
            .Select(point =>
            {
                var sun = point.Anchor switch
                {
                    TimeAnchor.Sunrise => solar?.Sunrise,
                    TimeAnchor.Sunset => solar?.Sunset,
                    _ => null,
                };
                var time =
                    point.Anchor == TimeAnchor.Clock || sun is null
                        ? SolarTimes.AtLocalTime(date, point.Time, zone)
                        : sun.Value.AddMinutes(point.OffsetMinutes);
                return new ResolvedTiming(
                    point,
                    time,
                    point.Anchor != TimeAnchor.Clock && sun is null
                );
            })
            .OrderBy(p => p.Time)
            .ToArray();
    }

    public static ColorProfile Color(MonitorProfile monitor, TimingPoint point) =>
        monitor.PointColors.GetValueOrDefault(point.Id, monitor.Manual);

    public static ScheduledColorEvent[] Build(
        SchedulePlan plan,
        MonitorProfile monitor,
        DateOnly date
    )
    {
        // Include surrounding days so offsets, midnight and DST retain their actual instants.
        return Enumerable
            .Range(-2, 5)
            .SelectMany(offset => ResolveDay(plan, monitor, date.AddDays(offset)))
            .GroupBy(p => p.Time.UtcTicks)
            // An explicit clock point wins if a moving solar event meets it.
            .Select(group => group.OrderBy(p => p.Point.Anchor == TimeAnchor.Clock ? 1 : 0).Last())
            .OrderBy(p => p.Time)
            .Select(p => new ScheduledColorEvent(
                p.Time,
                Color(monitor, p.Point),
                p.Point.TransitionMinutes ?? plan.TransitionMinutes,
                plan.SmoothTransitions,
                p.Point.Curve
            ))
            .ToArray();
    }

    public static ColorProfile Evaluate(
        SchedulePlan plan,
        MonitorProfile monitor,
        DateTimeOffset now
    )
    {
        if (!monitor.Scheduled || Points(plan, monitor).Count == 0)
            return monitor.Manual;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, plan.TimeZone).DateTime);
        return Evaluate(Build(plan, monitor, date), now, monitor.Manual);
    }

    public static ColorProfile Evaluate(
        IReadOnlyList<ScheduledColorEvent> events,
        DateTimeOffset now,
        ColorProfile fallback
    )
    {
        if (events.Count == 0)
            return fallback;
        var index = -1;
        for (var i = 0; i < events.Count && events[i].Time <= now; i++)
            index = i;
        if (index < 0)
            return fallback;
        var current = events[index];
        if (index == 0)
            return current.Profile;
        var previous = events[index - 1];
        var duration = Math.Min(
            current.TransitionMinutes,
            index + 1 < events.Count ? (events[index + 1].Time - current.Time).TotalMinutes : 1440
        );
        var elapsed = (now - current.Time).TotalMinutes;
        if (
            duration <= 0
            || elapsed >= duration
            || (previous.Profile.Mode != current.Profile.Mode && current.Curve is null)
        )
            return current.Profile;
        var t = Math.Clamp(elapsed / duration, 0, 1);
        if (current.Curve is { } curve)
            t = curve.Evaluate(t);
        else if (current.Smooth)
            t = t * t * (3 - 2 * t);
        var previousTemperature =
            previous.Profile.Mode == ColorMode.PureRed ? 500 : previous.Profile.Temperature;
        var currentTemperature =
            current.Profile.Mode == ColorMode.PureRed ? 500 : current.Profile.Temperature;
        return new(
            previous.Profile.Mode == current.Profile.Mode
                ? current.Profile.Mode
                : ColorMode.Temperature,
            previousTemperature + (currentTemperature - previousTemperature) * t,
            previous.Profile.Brightness
                + (current.Profile.Brightness - previous.Profile.Brightness) * t
        );
    }

    public static ScheduledColorEvent[] ForTransfer(
        SchedulePlan plan,
        MonitorProfile monitor,
        DateTimeOffset now
    )
    {
        if (!monitor.Scheduled)
            return [];
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, plan.TimeZone).DateTime);
        var events = Build(plan, monitor, date);
        var index = Array.FindLastIndex(events, p => p.Time <= now);
        return events.Skip(Math.Max(0, index - 1)).Where(p => p.Time <= now.AddHours(48)).ToArray();
    }
}
