using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Candlelight.Engine;
using Candlelight.Next;
using FluentAssertions;
using Xunit;

namespace Candlelight.Engine.Tests;

public class SchedulePlanSpecs
{
    private static readonly SolarLocation Povoa = new(
        "Póvoa de Varzim",
        41.38215,
        -8.75776,
        "Europe/Lisbon"
    );
    private static readonly DateOnly Day = new(2026, 10, 11);
    private static readonly ColorProfile Warm = new(ColorMode.Temperature, 2700, 1);
    private static readonly ColorProfile Red = new(ColorMode.PureRed, 500, .15);

    private static AppSettings Settings()
    {
        var plan = new SchedulePlan
        {
            Location = Povoa,
            Points = [new("morning", new(9, 0)), new("day", new(11, 0)), new("night", new(22, 0))],
        };
        return new()
        {
            Timing = plan,
            Monitors =
            [
                new()
                {
                    Id = "internal",
                    Name = "Internal",
                    Scheduled = true,
                    PointColors = new()
                    {
                        ["morning"] = Warm,
                        ["day"] = ColorProfile.Normal,
                        ["night"] = Warm with { Temperature = 500 },
                    },
                },
                new()
                {
                    Id = "oled",
                    Name = "OLED",
                    Scheduled = true,
                    Manual = Red,
                    PointColors = new()
                    {
                        ["morning"] = Warm with { Brightness = .4 },
                        ["day"] = ColorProfile.Normal with { Brightness = .4 },
                        ["night"] = Red,
                    },
                },
            ],
        };
    }

    [Fact]
    public void Shared_time_changes_keep_each_screens_colors_and_detaching_is_independent()
    {
        var s = Settings();
        var original = new Dictionary<string, ColorProfile>(s.Monitors[1].PointColors);
        var point = s.Timing.Points[0] with { Time = new(8, 0) };
        ScheduleEditing.SetPoint(s, "internal", point, Warm with { Temperature = 4000 });
        ScheduleTimeline.Points(s.Timing, s.Monitors[1])[0].Time.Should().Be(new TimeOnly(8, 0));
        s.Monitors[1].PointColors.Should().BeEquivalentTo(original);
        ScheduleEditing.SetShared(s, "oled", false, Day);
        ScheduleEditing.SetPoint(s, "internal", point with { Time = new(7, 0) }, Warm);
        ScheduleTimeline.Points(s.Timing, s.Monitors[1])[0].Time.Should().Be(new TimeOnly(8, 0));
        ScheduleEditing.SetShared(s, "oled", true, Day);
        ScheduleTimeline.Points(s.Timing, s.Monitors[1])[0].Time.Should().Be(new TimeOnly(7, 0));
        s.Monitors[1].PointColors["night"].Should().Be(Red);
    }

    [Fact]
    public void Adding_a_shared_point_samples_each_own_curve_and_copy_is_explicit()
    {
        var s = Settings();
        var id = ScheduleEditing.AddPoint(s, "internal", new(14, 0), null, Day);
        s.Monitors[0].PointColors[id].Brightness.Should().Be(1);
        s.Monitors[1].PointColors[id].Brightness.Should().Be(.4);
        ScheduleEditing.CopyColors(s, "oled", "internal", Day);
        s.Monitors[1].PointColors[id].Brightness.Should().Be(1);
        ScheduleEditing.RemovePoint(s, "oled", id);
        s.Timing.Points.Should().NotContain(p => p.Id == id);
        s.Monitors.Should().OnlyContain(m => !m.PointColors.ContainsKey(id));
    }

    [Fact]
    public void Solar_times_change_with_season_and_include_daylight_saving()
    {
        var summer = SolarTimes.Calculate(new(2026, 6, 21), Povoa);
        var winter = SolarTimes.Calculate(new(2026, 12, 21), Povoa);
        // Broad independent local-time bounds for Portugal's west coast.
        summer.Sunrise!.Value.TimeOfDay.TotalMinutes.Should().BeInRange(355, 370);
        summer.Sunset!.Value.TimeOfDay.TotalMinutes.Should().BeInRange(1265, 1285);
        winter.Sunrise!.Value.TimeOfDay.TotalMinutes.Should().BeInRange(470, 495);
        winter.Sunset!.Value.TimeOfDay.TotalMinutes.Should().BeInRange(1010, 1040);
        summer.Sunrise.Value.Offset.Should().Be(TimeSpan.FromHours(1));
        winter.Sunrise.Value.Offset.Should().Be(TimeSpan.Zero);
        var before = SolarTimes.Calculate(new(2026, 3, 28), Povoa).Sunrise!.Value;
        var after = SolarTimes.Calculate(new(2026, 3, 29), Povoa).Sunrise!.Value;
        (after.TimeOfDay - before.TimeOfDay).TotalMinutes.Should().BeInRange(55, 61);
    }

    [Fact]
    public void Dst_clock_points_resolve_the_gap_and_first_repeated_hour()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
        var spring = SolarTimes.AtLocalTime(new(2026, 3, 29), new(1, 30), zone);
        spring.Hour.Should().Be(2);
        spring.Minute.Should().Be(0);
        spring.Offset.Should().Be(TimeSpan.FromHours(1));
        var autumn = SolarTimes.AtLocalTime(new(2026, 10, 25), new(1, 30), zone);
        autumn.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Solar_offsets_use_real_dates_across_midnight_and_polar_events_have_fallbacks()
    {
        var s = Settings();
        s.Timing.Points =
        [
            new("morning", new(9, 0), TimeAnchor.Sunrise, -720),
            new("night", new(22, 0), TimeAnchor.Sunset),
        ];
        var sunrise = SolarTimes.Calculate(Day, Povoa).Sunrise!.Value;
        var nodes = ScheduleTimeline.ResolveDay(s.Timing, s.Monitors[0], Day);
        nodes.Single(n => n.Point.Id == "morning").Time.Should().Be(sunrise.AddHours(-12));
        nodes
            .Single(n => n.Point.Id == "morning")
            .Time.Date.Should()
            .Be(Day.AddDays(-1).ToDateTime(TimeOnly.MinValue));
        s.Timing.Location = new("Tromsø", 69.65, 18.96, "Europe/Oslo");
        var polar = SolarTimes.Calculate(new(2026, 6, 21), s.Timing.Location);
        polar.PolarDay.Should().BeTrue();
        polar.Sunrise.Should().BeNull();
        ScheduleTimeline
            .ResolveDay(s.Timing, s.Monitors[0], new(2026, 6, 21))
            .Should()
            .OnlyContain(n => n.UsedFallback);
    }

    [Fact]
    public void Global_smooth_transition_caps_at_next_point_and_keeps_red_exact()
    {
        var s = Settings();
        s.Timing.TransitionMinutes = 60;
        var now = SolarTimes.AtLocalTime(Day, new(11, 15), s.Timing.TimeZone);
        var result = ScheduleTimeline.Evaluate(s.Timing, s.Monitors[0], now);
        result.Temperature.Should().BeApproximately(2700 + 3900 * .15625, .001);
        ScheduleTimeline.Evaluate(s.Timing, s.Monitors[1], now).Brightness.Should().Be(.4);
        var red = ScheduleTimeline.Evaluate(
            s.Timing,
            s.Monitors[1],
            SolarTimes.AtLocalTime(Day, new(22, 0), s.Timing.TimeZone)
        );
        ChannelGain.FromProfile(red).Should().Be(new ChannelGain(.15, 0, 0));
        var midnight = ScheduleTimeline.Evaluate(
            s.Timing,
            s.Monitors[1],
            SolarTimes.AtLocalTime(Day.AddDays(1), new(0, 30), s.Timing.TimeZone)
        );
        midnight.Should().Be(Red);
        s.Timing.TransitionMinutes = 240;
        var halfway = ScheduleTimeline.Evaluate(
            s.Timing,
            s.Monitors[0],
            SolarTimes.AtLocalTime(Day, new(10, 0), s.Timing.TimeZone)
        );
        halfway.Temperature.Should().BeApproximately(1600, .001);
    }

    [Fact]
    public void Secure_transfer_preserves_the_same_dated_curve_and_rejects_unsorted_events()
    {
        var s = Settings();
        s.Timing.Points[0] = s.Timing.Points[0] with
        {
            Anchor = TimeAnchor.Sunrise,
            OffsetMinutes = 30,
        };
        var now = SolarTimes.Calculate(Day, Povoa).Sunrise!.Value.AddMinutes(35);
        var events = ScheduleTimeline.ForTransfer(s.Timing, s.Monitors[0], now);
        var secure = new SecureMonitorProfile("internal", true, Warm, [], events);
        var copy = SecureDesktopProfiles.Decode(SecureDesktopProfiles.Encode([secure])).Single();
        copy.EvaluateAt(now).Should().Be(ScheduleTimeline.Evaluate(s.Timing, s.Monitors[0], now));
        Action invalid = () =>
            SecureDesktopProfiles.Encode([secure with { Events = events.Reverse().ToArray() }]);
        invalid.Should().Throw<InvalidDataException>();
        invalid = () =>
            SecureDesktopProfiles.Encode([secure with { Events = [events[0], events[0]] }]);
        invalid.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Migration_reuses_existing_hours_and_preserves_distinct_saved_schedules_and_presets()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CandlelightSchedule-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Settings.json");
            var legacy = new AppSettings
            {
                Schema = 1,
                Monitors =
                [
                    new()
                    {
                        Id = "internal",
                        Name = "Internal",
                        Scheduled = true,
                        Schedule = [new(new(9, 0), Warm, 15), new(new(22, 0), Red, 15)],
                    },
                    new()
                    {
                        Id = "oled",
                        Name = "OLED",
                        Scheduled = true,
                    },
                    new()
                    {
                        Id = "other",
                        Name = "Other",
                        Scheduled = true,
                        Schedule = [new(new(20, 0), Warm with { Brightness = .6 }, 10)],
                    },
                ],
            };
            File.WriteAllText(path, JsonSerializer.Serialize(legacy, ProfileStore.Json));
            var original = File.ReadAllText(path);
            var store = new ProfileStore(path);
            var migrated = store.Read();
            migrated.Schema.Should().Be(2);
            migrated.Timing.TransitionMinutes.Should().Be(15);
            migrated
                .Timing.Points.Select(p => p.Time)
                .Should()
                .Equal(new TimeOnly(9, 0), new TimeOnly(22, 0));
            migrated
                .Monitors[1]
                .PointColors.Should()
                .BeEquivalentTo(migrated.Monitors[0].PointColors);
            migrated.Monitors[2].UseSharedSchedule.Should().BeFalse();
            migrated.Monitors[2].OwnTiming.Single().Time.Should().Be(new TimeOnly(20, 0));
            migrated.Presets.Should().BeEquivalentTo(legacy.Presets);
            File.ReadAllText(Directory.GetFiles(directory, "*.bak").Single()).Should().Be(original);
            new ProfileStore(path)
                .Read()
                .Timing.Points.Should()
                .BeEquivalentTo(migrated.Timing.Points);
            Action badEdit = () => store.Update(s => s.Timing.Points.Add(s.Timing.Points[0]));
            badEdit.Should().Throw<ArgumentException>();
            store.Read().Timing.Points.Should().HaveCount(2);
            JsonSerializer
                .Serialize(migrated, ProfileStore.Json)
                .Should()
                .NotContain("AdjustmentRules");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
