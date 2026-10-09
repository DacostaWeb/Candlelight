using System;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace LightBulb.Core.Tests;

public class DailyScheduleSpecs
{
    private static readonly ColorConfiguration Day = new(6600, 1);
    private static readonly ColorConfiguration Red = new(500, 0.15);

    [Fact]
    public void An_empty_schedule_uses_its_manual_fallback() =>
        DailySchedule.Evaluate([], new TimeOnly(22, 0), Red).Should().Be(Red);

    [Fact]
    public void One_point_stays_constant_all_day() =>
        DailySchedule
            .Evaluate(
                [new(new TimeOnly(22, 0), Red, TimeSpan.FromHours(1))],
                new TimeOnly(7, 0),
                Day
            )
            .Should()
            .Be(Red);

    [Theory]
    [InlineData(21, 59, 6600, 1)]
    [InlineData(22, 0, 500, 0.15)]
    [InlineData(0, 0, 500, 0.15)]
    [InlineData(6, 59, 500, 0.15)]
    [InlineData(7, 0, 6600, 1)]
    public void Immediate_changes_hold_until_the_next_point(
        int hour,
        int minute,
        double temperature,
        double brightness
    )
    {
        SchedulePoint[] points =
        [
            new(new(7, 0), Day, TimeSpan.Zero),
            new(new(22, 0), Red, TimeSpan.Zero),
        ];
        DailySchedule
            .Evaluate(points, new(hour, minute), Day)
            .Should()
            .Be(new ColorConfiguration(temperature, brightness));
    }

    [Fact]
    public void A_transition_starts_at_the_given_time_and_crosses_midnight()
    {
        SchedulePoint[] points =
        [
            new(new(7, 0), Day, TimeSpan.Zero),
            new(new(23, 30), Red, TimeSpan.FromHours(1)),
        ];
        DailySchedule.Evaluate(points, new(23, 30), Day).Should().Be(Day);
        var middle = DailySchedule.Evaluate(points, new(0, 0), Day);
        middle.Temperature.Should().Be(3550);
        middle.Brightness.Should().BeApproximately(0.575, 0.00001);
        DailySchedule.Evaluate(points, new(0, 30), Day).Should().Be(Red);
    }

    [Fact]
    public void Long_transitions_are_capped_at_the_next_point_without_a_jump()
    {
        SchedulePoint[] points =
        [
            new(new(7, 0), Day, TimeSpan.Zero),
            new(new(22, 0), Red, TimeSpan.FromHours(4)),
            new(new(23, 0), new(2700, 0.6), TimeSpan.FromHours(1)),
        ];
        var before = DailySchedule.Evaluate(points, new(22, 59, 59), Day);
        before.Temperature.Should().BeApproximately(500, 2);
        DailySchedule.Evaluate(points, new(23, 0), Day).Should().Be(Red);
    }

    [Fact]
    public void Unsorted_points_have_the_same_result()
    {
        SchedulePoint[] points =
        [
            new(new(22, 0), Red, TimeSpan.Zero),
            new(new(7, 0), Day, TimeSpan.Zero),
        ];
        DailySchedule.Evaluate(points, new(12, 0), Red).Should().Be(Day);
    }

    [Fact]
    public void Two_monitors_can_use_different_values_at_the_same_instant()
    {
        var oled = new DisplayProfile
        {
            Id = "oled",
            Name = "OLED",
            Mode = DisplayControlMode.Manual,
            ManualConfiguration = Red,
        };
        var internalDisplay = new DisplayProfile
        {
            Id = "internal",
            Name = "Interno",
            Mode = DisplayControlMode.Manual,
            ManualConfiguration = new(2700, 1),
        };
        var instant = new DateTimeOffset(2026, 10, 9, 23, 0, 0, TimeSpan.Zero);
        var solar = new SolarTimes(new(7, 0), new(18, 0));
        oled.Evaluate(solar, TimeSpan.Zero, 0, instant).Should().Be(Red);
        internalDisplay.Evaluate(solar, TimeSpan.Zero, 0, instant).Brightness.Should().Be(1);
        (oled with { IsEnabled = false })
            .Evaluate(solar, TimeSpan.Zero, 0, instant)
            .Should()
            .Be(ColorConfiguration.Default);
    }

    [Fact]
    public void Monitor_identity_and_schedule_survive_json_round_trip()
    {
        var profile = new DisplayProfile
        {
            Id = @"\\?\DISPLAY#OLED#unique-device",
            Name = "OLED",
            Mode = DisplayControlMode.Schedule,
            Schedule = [new(new(22, 0), Red, TimeSpan.FromMinutes(30))],
        };
        var restored = JsonSerializer.Deserialize<DisplayProfile>(
            JsonSerializer.Serialize(profile)
        )!;
        restored.Id.Should().Be(profile.Id);
        restored.Mode.Should().Be(DisplayControlMode.Schedule);
        restored.Schedule.Should().Equal(profile.Schedule);
    }
}
