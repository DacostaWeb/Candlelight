using System;
using System.Collections.Generic;

namespace LightBulb.Core;

public enum DisplayControlMode
{
    SunCycle,
    Manual,
    Schedule,
}

// An interface path identifies a monitor independently of Windows' DISPLAY1/2 numbering.
public sealed record DisplayProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DisplayControlMode Mode { get; init; }
    public ColorConfiguration ManualConfiguration { get; init; } = ColorConfiguration.Default;
    public ColorConfiguration DayConfiguration { get; init; } = ColorConfiguration.Default;
    public ColorConfiguration NightConfiguration { get; init; } = new(3900, 0.85);
    public IReadOnlyList<SchedulePoint> Schedule { get; init; } = [];

    public ColorConfiguration Evaluate(
        SolarTimes solarTimes,
        TimeSpan transitionDuration,
        double transitionOffset,
        DateTimeOffset instant
    ) =>
        !IsEnabled ? ColorConfiguration.Default
        : Mode == DisplayControlMode.Manual ? ManualConfiguration
        : Mode == DisplayControlMode.Schedule
            ? DailySchedule.Evaluate(
                Schedule,
                TimeOnly.FromDateTime(instant.DateTime),
                ManualConfiguration
            )
        : Cycle.InterpolateConfiguration(
            solarTimes,
            DayConfiguration,
            NightConfiguration,
            transitionDuration,
            transitionOffset,
            instant
        );
}

public sealed record ColorPreset(string Id, string Name, ColorConfiguration Configuration);

public sealed record SchedulePoint(
    TimeOnly Time,
    ColorConfiguration Configuration,
    TimeSpan Transition
);
