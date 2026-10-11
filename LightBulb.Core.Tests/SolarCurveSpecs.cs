using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Candlelight.Engine;
using Candlelight.Next;
using FluentAssertions;
using Xunit;

namespace Candlelight.Engine.Tests;

public class SolarCurveSpecs
{
    [Fact]
    public void Extra_solar_points_and_detach_rejoin_keep_their_solar_anchor_and_targets()
    {
        var s = Settings();
        var morning = new TimingPoint(
            "rise",
            new(7, 0),
            TimeAnchor.Sunrise,
            -30,
            90,
            TransitionCurve.SlowStart
        );
        var evening = new TimingPoint(
            "set",
            new(18, 0),
            TimeAnchor.Sunset,
            -60,
            120,
            TransitionCurve.SlowEnd
        );
        ScheduleEditing.ApplySolarCycle(s, "internal", morning, evening);
        var day = new DateOnly(2026, 6, 21);
        var instant = SolarTimes.Calculate(day, Povoa).Sunset!.Value.AddMinutes(60);
        var id = ScheduleEditing.AddPoint(
            s,
            "oled",
            TimeOnly.FromTimeSpan(instant.TimeOfDay),
            Night,
            day
        );
        var added = s.Timing.Points.Single(p => p.Id == id);
        added.Anchor.Should().Be(TimeAnchor.Sunset);
        added.OffsetMinutes.Should().Be(60);
        ScheduleEditing.SetShared(s, "oled", false, day);
        ScheduleEditing.SetShared(s, "oled", true, day);
        s.Monitors[1].PointColors["set"].Should().Be(Night);
        s.Monitors[1].PointColors["rise"].Brightness.Should().Be(.8);
    }

    private static readonly SolarLocation Povoa = new(
        "Póvoa de Varzim",
        41.38215,
        -8.75776,
        "Europe/Lisbon"
    );
    private static readonly ColorProfile Night = new(ColorMode.PureRed, 500, .15);

    private static AppSettings Settings() =>
        new()
        {
            Timing = new()
            {
                Location = Povoa,
                Points = [new("day", new(9, 0)), new("night", new(22, 0))],
            },
            Monitors =
            [
                new()
                {
                    Id = "internal",
                    Name = "Internal",
                    Scheduled = true,
                    PointColors = new()
                    {
                        ["day"] = ColorProfile.Normal,
                        ["night"] = new(ColorMode.Temperature, 2700, 1),
                    },
                },
                new()
                {
                    Id = "oled",
                    Name = "OLED",
                    Scheduled = true,
                    PointColors = new()
                    {
                        ["day"] = ColorProfile.Normal with { Brightness = .8 },
                        ["night"] = Night,
                    },
                },
            ],
        };

    [Fact]
    public void Bezier_curves_are_monotone_exact_at_endpoints_and_match_reference_shapes()
    {
        foreach (
            var curve in new[]
            {
                TransitionCurve.Linear,
                TransitionCurve.Smooth,
                TransitionCurve.SlowStart,
                TransitionCurve.SlowEnd,
                new TransitionCurve(0, 0, 0, 1),
                new TransitionCurve(1, 0, 1, 1),
            }
        )
        {
            curve.Validate();
            curve.Evaluate(0).Should().Be(0);
            curve.Evaluate(1).Should().Be(1);
            Enumerable
                .Range(0, 101)
                .Select(i => curve.Evaluate(i / 100d))
                .Should()
                .BeInAscendingOrder();
        }
        TransitionCurve.Linear.Evaluate(.25).Should().BeApproximately(.25, 1e-8);
        TransitionCurve.Smooth.Evaluate(.25).Should().BeApproximately(.15625, 1e-8);
        TransitionCurve.SlowStart.Evaluate(.5).Should().BeLessThan(.5);
        TransitionCurve.SlowEnd.Evaluate(.5).Should().BeGreaterThan(.5);
        Action invalid = () => new TransitionCurve(.8, 0, .2, 1).Validate();
        invalid.Should().Throw<ArgumentException>();
        invalid = () => new TransitionCurve(0, double.NaN, 1, 1).Validate();
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Solar_cycle_keeps_own_colors_and_easing_relative_to_the_sun_on_every_day_of_year()
    {
        var s = Settings();
        var morning = new TimingPoint(
            "rise",
            new(7, 0),
            TimeAnchor.Sunrise,
            -30,
            90,
            TransitionCurve.SlowStart
        );
        var evening = new TimingPoint(
            "set",
            new(18, 0),
            TimeAnchor.Sunset,
            -60,
            120,
            TransitionCurve.SlowEnd
        );
        ScheduleEditing.ApplySolarCycle(s, "internal", morning, evening);
        s.Monitors[1].PointColors["set"].Should().Be(Night);
        s.Monitors[0].PointColors["rise"].Brightness.Should().Be(1);
        s.Monitors[1].PointColors["rise"].Brightness.Should().Be(.8);
        var start = new DateOnly(2026, 1, 1);
        for (var day = 0; day < 365; day++)
        {
            var date = start.AddDays(day);
            var sun = SolarTimes.Calculate(date, Povoa);
            var nodes = ScheduleTimeline.ResolveDay(s.Timing, s.Monitors[1], date);
            nodes[0].Time.Should().Be(sun.Sunrise!.Value.AddMinutes(-30));
            nodes[1].Time.Should().Be(sun.Sunset!.Value.AddMinutes(-60));
            var end = nodes[1].Time.AddMinutes(120);
            ScheduleTimeline.Evaluate(s.Timing, s.Monitors[1], end).Should().Be(Night);
            var middle = ScheduleTimeline.Evaluate(
                s.Timing,
                s.Monitors[1],
                nodes[1].Time.AddMinutes(60)
            );
            middle.Mode.Should().Be(ColorMode.Temperature);
            middle
                .Temperature.Should()
                .BeApproximately(6600 + (500 - 6600) * TransitionCurve.SlowEnd.Evaluate(.5), .0001);
            middle
                .Brightness.Should()
                .BeApproximately(.8 + (.15 - .8) * TransitionCurve.SlowEnd.Evaluate(.5), .0001);
        }
    }

    [Fact]
    public void Temporary_time_curve_and_manual_edits_leave_program_intact_and_reset_at_exact_midnight()
    {
        var s = Settings();
        var original = JsonSerializer.Serialize(s, ProfileStore.Json);
        var now = SolarTimes.AtLocalTime(new(2026, 10, 11), new(19, 0), s.Timing.TimeZone);
        ScheduleSession.EditToday(
            s,
            now,
            view =>
            {
                view.Timing.Points[0] = view.Timing.Points[0] with
                {
                    Anchor = TimeAnchor.Sunrise,
                    OffsetMinutes = -45,
                    Curve = TransitionCurve.SlowEnd,
                    TransitionMinutes = 120,
                };
                view.Monitors[1].PointColors["day"] = Night;
                view.Monitors[0].Manual = Night;
                view.Monitors[0].Scheduled = false;
            }
        );
        JsonSerializer.Serialize(s with { Today = null }, ProfileStore.Json).Should().Be(original);
        var midnight = s.Today!.ExpiresAt;
        TimeZoneInfo.ConvertTime(midnight, s.Timing.TimeZone).TimeOfDay.Should().Be(TimeSpan.Zero);
        var effective = ScheduleSession.Effective(s, midnight.AddTicks(-1));
        effective.Monitors[0].Scheduled.Should().BeFalse();
        effective.Timing.Points[0].Curve.Should().Be(TransitionCurve.SlowEnd);
        ScheduleSession.Effective(s, midnight).Timing.Should().BeSameAs(s.Timing);
        ScheduleSession.Effective(s, midnight.AddDays(3)).Monitors[0].Scheduled.Should().BeTrue();
        ScheduleSession.EditToday(
            s,
            midnight.AddMinutes(10),
            view => view.Monitors[0].PointColors["day"] = Night
        );
        s.Today!.Timing.Points[0].Anchor.Should().Be(TimeAnchor.Clock);
    }

    [Theory]
    [InlineData(3, 29, 23)]
    [InlineData(10, 25, 25)]
    public void Temporary_lifetime_uses_local_midnight_across_dst(int month, int day, int hours)
    {
        var s = Settings();
        var now = SolarTimes.AtLocalTime(
            new(2026, month, day),
            TimeOnly.MinValue,
            s.Timing.TimeZone
        );
        ScheduleSession.EditToday(s, now, view => view.Monitors[0].Enabled = false);
        (s.Today!.ExpiresAt - now).Should().Be(TimeSpan.FromHours(hours));
        ScheduleSession.Effective(s, s.Today.ExpiresAt).Monitors[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Secure_desktop_retains_custom_curves_and_resumes_base_without_a_new_packet_at_midnight()
    {
        var s = Settings();
        var now = SolarTimes.AtLocalTime(new(2026, 10, 11), new(22, 5), s.Timing.TimeZone);
        ScheduleSession.EditToday(
            s,
            now,
            view =>
            {
                view.Timing.Points[1] = view.Timing.Points[1] with
                {
                    TransitionMinutes = 120,
                    Curve = TransitionCurve.SlowEnd,
                };
                view.Monitors[0].Enabled = false;
            }
        );
        var profiles = SecureDesktopProfiles.Decode(
            SecureDesktopProfiles.Encode(ScheduleSession.ForTransfer(s, now))
        );
        var secure = profiles.Single(m => m.Id == "oled");
        var active = ScheduleSession.Effective(s, now);
        secure
            .EvaluateAt(now)
            .Should()
            .Be(ScheduleTimeline.Evaluate(active.Timing, active.Monitors[1], now));
        profiles[0].EnabledAt(now).Should().BeFalse();
        var midnight = s.Today!.ExpiresAt;
        profiles[0].EnabledAt(midnight).Should().BeTrue();
        secure
            .EvaluateAt(midnight)
            .Should()
            .Be(ScheduleTimeline.Evaluate(s.Timing, s.Monitors[1], midnight));
        Action invalid = () =>
            SecureDesktopProfiles.Encode([
                secure with
                {
                    Today = secure.Today! with { ExpiresAt = now.AddDays(2) },
                },
            ]);
        invalid.Should().Throw<InvalidDataException>();
        invalid = () =>
            SecureDesktopProfiles.Encode([
                secure with
                {
                    Events = [new(now, Night, 30, true, new(0, 1, 1, 0))],
                },
            ]);
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Schema_two_migration_and_restart_preserve_program_and_expire_temporary_file()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CandlelightSolar-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Settings.json");
            var initial = Settings() with { Schema = 2 };
            File.WriteAllText(path, JsonSerializer.Serialize(initial, ProfileStore.Json));
            var original = File.ReadAllText(path);
            var store = new ProfileStore(path);
            store.Read().Schema.Should().Be(3);
            File.ReadAllText(Directory.GetFiles(directory, "*.bak").Single()).Should().Be(original);
            store.Read().Timing.Should().BeEquivalentTo(initial.Timing);
            var now = SolarTimes.AtLocalTime(
                new(2026, 10, 11),
                new(23, 45),
                initial.Timing.TimeZone
            );
            store.Update(s =>
                ScheduleSession.EditToday(s, now, view => view.Monitors[1].Manual = Night)
            );
            var restarted = new ProfileStore(path);
            ScheduleSession.IsActive(restarted.Read(), now.AddMinutes(10)).Should().BeTrue();
            restarted.ExpireToday(now.AddMinutes(15)).Should().BeTrue();
            restarted.Read().Today.Should().BeNull();
            new ProfileStore(path).Read().Timing.Should().BeEquivalentTo(initial.Timing);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
