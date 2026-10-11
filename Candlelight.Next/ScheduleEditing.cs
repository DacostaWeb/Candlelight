using System;
using System.Linq;
using Candlelight.Engine;

namespace Candlelight.Next;

internal static class ScheduleEditing
{
    internal static MonitorProfile Monitor(AppSettings settings, string id) =>
        settings.Monitors.Single(m => m.Id == id);

    internal static void SetPoint(
        AppSettings settings,
        string monitorId,
        TimingPoint point,
        ColorProfile color
    )
    {
        var monitor = Monitor(settings, monitorId);
        var points = monitor.UseSharedSchedule ? settings.Timing.Points : monitor.OwnTiming;
        var index = points.FindIndex(p => p.Id == point.Id);
        if (index < 0)
            throw new ArgumentException("Este ponto já não existe.");
        points[index] = point;
        monitor.PointColors[point.Id] = color.Validate();
    }

    internal static string AddPoint(
        AppSettings settings,
        string monitorId,
        TimeOnly time,
        ColorProfile? selectedColor,
        DateOnly date
    )
    {
        var monitor = Monitor(settings, monitorId);
        var points = monitor.UseSharedSchedule ? settings.Timing.Points : monitor.OwnTiming;
        var point = new TimingPoint(Guid.NewGuid().ToString("N"), time);
        var instant = SolarTimes.AtLocalTime(date, time, settings.Timing.TimeZone);
        var targets = monitor.UseSharedSchedule
            ? settings.Monitors.Where(m => m.UseSharedSchedule).ToArray()
            : [monitor];
        var colors = targets.ToDictionary(
            m => m.Id,
            m => ScheduleTimeline.Evaluate(settings.Timing, m with { Scheduled = true }, instant)
        );
        points.Add(point);
        foreach (var target in targets)
            target.PointColors[point.Id] =
                target.Id == monitorId && selectedColor is not null
                    ? selectedColor
                    : colors[target.Id];
        return point.Id;
    }

    internal static void RemovePoint(AppSettings settings, string monitorId, string pointId)
    {
        var monitor = Monitor(settings, monitorId);
        var points = monitor.UseSharedSchedule ? settings.Timing.Points : monitor.OwnTiming;
        if (points.Count <= 1)
            throw new ArgumentException("Mantém pelo menos um ponto no horário.");
        points.RemoveAll(p => p.Id == pointId);
        foreach (
            var target in settings.Monitors.Where(m =>
                m == monitor || monitor.UseSharedSchedule && m.UseSharedSchedule
            )
        )
            target.PointColors.Remove(pointId);
    }

    internal static void SetShared(
        AppSettings settings,
        string monitorId,
        bool shared,
        DateOnly date
    )
    {
        var monitor = Monitor(settings, monitorId);
        if (monitor.UseSharedSchedule == shared)
            return;
        if (!shared)
            monitor.OwnTiming = settings.Timing.Points.ToList();
        else
        {
            var source = monitor with { Scheduled = true };
            var nodes = ScheduleTimeline.ResolveDay(
                settings.Timing,
                monitor with
                {
                    UseSharedSchedule = true,
                },
                date
            );
            monitor.PointColors = nodes.ToDictionary(
                p => p.Point.Id,
                p =>
                    ScheduleTimeline.Evaluate(
                        settings.Timing,
                        source,
                        p.Time.AddMinutes(settings.Timing.TransitionMinutes)
                    )
            );
            monitor.OwnTiming = [];
        }
        monitor.UseSharedSchedule = shared;
    }

    internal static void CopyColors(
        AppSettings settings,
        string targetId,
        string sourceId,
        DateOnly date
    )
    {
        var target = Monitor(settings, targetId);
        var source = Monitor(settings, sourceId);
        var nodes = ScheduleTimeline.ResolveDay(settings.Timing, target, date);
        target.PointColors = nodes.ToDictionary(
            p => p.Point.Id,
            p =>
                source.PointColors.TryGetValue(p.Point.Id, out var color)
                    ? color
                    : ScheduleTimeline.Evaluate(
                        settings.Timing,
                        source with
                        {
                            Scheduled = true,
                        },
                        p.Time.AddMinutes(settings.Timing.TransitionMinutes)
                    )
        );
    }
}
