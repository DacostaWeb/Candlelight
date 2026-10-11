using System;
using System.Collections.Generic;
using System.Linq;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed record TodaySchedule(
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    SchedulePlan Timing,
    List<MonitorProfile> Monitors
);

internal static class ScheduleSession
{
    internal static bool IsActive(AppSettings settings, DateTimeOffset now) =>
        settings.Today is { } today && now >= today.StartsAt && now < today.ExpiresAt;

    internal static AppSettings Effective(AppSettings settings, DateTimeOffset now)
    {
        if (!IsActive(settings, now))
            return settings;
        var today = settings.Today!;
        return settings with
        {
            Timing = today.Timing,
            Monitors = settings
                .Monitors.Select(m => today.Monitors.FirstOrDefault(t => t.Id == m.Id) ?? m)
                .ToList(),
        };
    }

    internal static void EditToday(
        AppSettings settings,
        DateTimeOffset now,
        Action<AppSettings> edit
    )
    {
        if (!IsActive(settings, now))
        {
            var baseline = ProfileStore.Clone(settings with { Today = null });
            var zone = baseline.Timing.TimeZone;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            settings.Today = new(
                now,
                SolarTimes.AtLocalTime(date.AddDays(1), TimeOnly.MinValue, zone),
                baseline.Timing,
                baseline.Monitors
            );
        }
        var view = Effective(settings, now);
        edit(view);
        settings.Today = settings.Today! with { Timing = view.Timing, Monitors = view.Monitors };
    }

    internal static SecureMonitorProfile[] ForTransfer(AppSettings settings, DateTimeOffset now)
    {
        var effective = Effective(settings, now);
        return settings
            .Monitors.Select(m =>
            {
                SecureScheduleOverride? temporary = null;
                if (IsActive(settings, now))
                {
                    var current = effective.Monitors.Single(t => t.Id == m.Id);
                    temporary = new(
                        settings.Today!.StartsAt,
                        settings.Today.ExpiresAt,
                        current.Enabled,
                        current.Manual,
                        ScheduleTimeline.ForTransfer(effective.Timing, current, now)
                    );
                }
                return new SecureMonitorProfile(
                    m.Id,
                    m.Enabled,
                    m.Manual,
                    [],
                    ScheduleTimeline.ForTransfer(settings.Timing, m, now),
                    temporary
                );
            })
            .ToArray();
    }
}
