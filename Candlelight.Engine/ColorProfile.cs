namespace Candlelight.Engine;

public enum ColorMode
{
    Temperature,
    PureRed,
}

public sealed record ColorProfile(ColorMode Mode, double Temperature, double Brightness)
{
    public static ColorProfile Normal { get; } = new(ColorMode.Temperature, 6600, 1);

    public ColorProfile Validate()
    {
        if (!Enum.IsDefined(Mode) || !double.IsFinite(Temperature) || !double.IsFinite(Brightness))
            throw new ArgumentException("Invalid color profile.");
        return this with
        {
            Temperature = Math.Clamp(Temperature, 500, 12000),
            Brightness = Math.Clamp(Brightness, 0.01, 1),
        };
    }
}

public sealed record ChannelGain(double Red, double Green, double Blue)
{
    public static ChannelGain FromProfile(ColorProfile profile)
    {
        profile = profile.Validate();
        if (profile.Mode == ColorMode.PureRed)
            return new(profile.Brightness, 0, 0);
        var temperature = profile.Temperature / 100;
        var red =
            temperature > 66
                ? Math.Clamp(329.698727446 * Math.Pow(temperature - 60, -0.1332047592) / 255, 0, 1)
                : 1;
        var green =
            temperature > 66
                ? Math.Clamp(288.1221695283 * Math.Pow(temperature - 60, -0.0755148492) / 255, 0, 1)
                : Math.Clamp((99.4708025861 * Math.Log(temperature) - 161.1195681661) / 255, 0, 1);
        var blue =
            temperature >= 66 ? 1
            : temperature <= 19 ? 0
            : Math.Clamp(
                (138.5177312231 * Math.Log(temperature - 10) - 305.0447927307) / 255,
                0,
                1
            );
        // The normal preset must be a true identity, not a slightly tinted white.
        if (profile.Temperature == 6600)
            red = green = blue = 1;
        return new(red * profile.Brightness, green * profile.Brightness, blue * profile.Brightness);
    }
}

public sealed record MonitorProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; set; } = true;
    public ColorProfile Manual { get; set; } = new(ColorMode.Temperature, 2700, 1);
    public bool Scheduled { get; set; }
    public List<SchedulePoint> Schedule { get; set; } = [];

    public ColorProfile Evaluate(TimeOnly time) =>
        Scheduled && Schedule.Count > 0 ? DailySchedule.Evaluate(Schedule, time) : Manual;
}

public sealed record SchedulePoint(TimeOnly Time, ColorProfile Profile, int TransitionMinutes = 0);

public static class DailySchedule
{
    public static ColorProfile Evaluate(IEnumerable<SchedulePoint> points, TimeOnly time)
    {
        var ordered = points.OrderBy(p => p.Time).ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("A schedule needs at least one point.");
        if (ordered.Select(p => p.Time).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Two schedule points cannot have the same time.");
        if (ordered.Any(p => p.TransitionMinutes is < 0 or > 1440))
            throw new ArgumentException("Invalid transition duration.");
        var index = Array.FindLastIndex(ordered, p => p.Time <= time);
        if (index < 0)
            index = ordered.Length - 1;
        var current = ordered[index];
        var previous = ordered[(index + ordered.Length - 1) % ordered.Length];
        var elapsed = (time.ToTimeSpan() - current.Time.ToTimeSpan()).TotalMinutes;
        if (elapsed < 0)
            elapsed += 1440;
        var next = ordered[(index + 1) % ordered.Length];
        var available = (next.Time.ToTimeSpan() - current.Time.ToTimeSpan()).TotalMinutes;
        if (available <= 0)
            available += 1440;
        var duration = Math.Min(current.TransitionMinutes, available);
        if (duration == 0 || elapsed >= duration || ordered.Length == 1)
            return current.Profile.Validate();
        // Never introduce green/blue in an explicitly requested red-only mode.
        if (current.Profile.Mode != previous.Profile.Mode)
            return current.Profile.Validate();
        var ratio = elapsed / duration;
        return new ColorProfile(
            current.Profile.Mode,
            previous.Profile.Temperature
                + (current.Profile.Temperature - previous.Profile.Temperature) * ratio,
            previous.Profile.Brightness
                + (current.Profile.Brightness - previous.Profile.Brightness) * ratio
        ).Validate();
    }
}
