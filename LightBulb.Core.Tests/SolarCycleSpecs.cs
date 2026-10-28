using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace LightBulb.Core.Tests;

public class SolarCycleSpecs
{
    private static readonly GeoLocation Povoa = new(41.38215, -8.75776);
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(
        "GMT Standard Time"
    );
    private static readonly SolarTimes Fixed = new(new(7, 0), new(18, 0));
    private static readonly ColorConfiguration Day = new(2700, 1);
    private static readonly ColorConfiguration Night = new(500, 0.25);

    private static ColorConfiguration At(
        DateTimeOffset instant,
        SolarCycleOptions? options = null
    ) => SolarCycle.Evaluate(options ?? new(), Day, Night, Povoa, Fixed, instant, Lisbon);

    [Theory]
    [InlineData(2026)]
    [InlineData(2028)]
    public void Sunrise_starts_at_night_values_and_sunset_finishes_at_night_values_every_day(
        int year
    )
    {
        var day = new DateOnly(year, 1, 1);
        while (day.Year == year)
        {
            var (rise, set) = SolarCycle.Events(day, Povoa, Fixed, Lisbon);
            At(rise).Should().Be(Night, $"sunrise on {day}");
            At(rise.AddHours(2)).Should().Be(Day, $"morning end on {day}");
            At(set.AddHours(-2)).Should().Be(Day, $"evening start on {day}");
            At(set).Should().Be(Night, $"sunset on {day}");
            At(rise.AddHours(1)).Temperature.Should().BeInRange(500, 2700);
            At(set.AddHours(-1)).Brightness.Should().BeInRange(0.25, 1);
            day = day.AddDays(1);
        }
    }

    [Fact]
    public void Solar_times_follow_both_daylight_saving_changes()
    {
        DateTime Rise(int month, int day) =>
            SolarCycle.Events(new(2026, month, day), Povoa, Fixed, Lisbon).Sunrise.DateTime;
        (Rise(3, 29).TimeOfDay - Rise(3, 28).TimeOfDay).TotalMinutes.Should().BeInRange(45, 75);
        (Rise(10, 25).TimeOfDay - Rise(10, 24).TimeOfDay).TotalMinutes.Should().BeInRange(-75, -45);
    }

    [Fact]
    public void Normal_colors_do_not_replace_a_monitor_program()
    {
        var oled = new DisplayProfile
        {
            Id = "oled",
            Name = "OLED",
            Mode = DisplayControlMode.SunCycle,
            DayConfiguration = Day,
            NightConfiguration = Night,
            SolarCycle = new(),
        };
        var internalDisplay = oled with { Id = "internal", NightConfiguration = new(500, 1) };
        var sunset = SolarCycle.Events(new(2026, 10, 28), Povoa, Fixed, Lisbon).Sunset;
        var paused = oled with { IsNormalColors = true };
        paused
            .Evaluate(Fixed, TimeSpan.Zero, 0, sunset, Povoa, Lisbon)
            .Should()
            .Be(ColorConfiguration.Default);
        (paused with { IsNormalColors = false })
            .Evaluate(Fixed, TimeSpan.Zero, 0, sunset, Povoa, Lisbon)
            .Should()
            .Be(Night);
        internalDisplay
            .Evaluate(Fixed, TimeSpan.Zero, 0, sunset, Povoa, Lisbon)
            .Should()
            .Be(new ColorConfiguration(500, 1));
        paused.SolarCycle.Should().Be(oled.SolarCycle);
        paused.NightConfiguration.Should().Be(Night);
    }

    [Fact]
    public void Linear_and_shaped_curves_produce_different_values_between_the_same_endpoints()
    {
        var rise = SolarCycle.Events(new(2026, 10, 28), Povoa, Fixed, Lisbon).Sunrise;
        var linear = At(rise.AddMinutes(30), new() { SunriseCurve = TransitionCurve.Linear });
        linear.Temperature.Should().BeApproximately(1050, 0.001);
        linear.Brightness.Should().BeApproximately(0.4375, 0.00001);
        At(rise.AddMinutes(30), new() { SunriseCurve = TransitionCurve.Early })
            .Temperature.Should()
            .BeGreaterThan(linear.Temperature);
        At(rise.AddMinutes(30), new() { SunriseCurve = TransitionCurve.Late })
            .Temperature.Should()
            .BeLessThan(linear.Temperature);
    }

    [Fact]
    public void Transition_crossing_midnight_uses_the_previous_days_event()
    {
        var options = new SolarCycleOptions
        {
            SunriseMinutes = 120,
            SunriseCurve = TransitionCurve.Linear,
            SunsetMinutes = 0,
        };
        var lateRise = new SolarTimes(new(23, 0), new(18, 0));
        var instant = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        SolarCycle
            .Evaluate(options, Day, Night, null, lateRise, instant, TimeZoneInfo.Utc)
            .Temperature.Should()
            .BeApproximately(1600, 0.001);
    }

    [Fact]
    public void Zero_duration_switches_at_the_solar_event()
    {
        var options = new SolarCycleOptions { SunriseMinutes = 0, SunsetMinutes = 0 };
        var (rise, set) = SolarCycle.Events(new(2026, 10, 28), Povoa, Fixed, Lisbon);
        At(rise.AddTicks(-1), options).Should().Be(Night);
        At(rise, options).Should().Be(Day);
        At(set.AddTicks(-1), options).Should().Be(Day);
        At(set, options).Should().Be(Night);
    }

    [Fact]
    public void Invalid_curve_handles_are_bounded_and_monotonic()
    {
        var curve = new TransitionCurve(double.NaN, 2, -1, double.PositiveInfinity);
        var values = Enumerable.Range(0, 101).Select(i => curve.Evaluate(i / 100d)).ToArray();
        values.Should().OnlyContain(value => double.IsFinite(value) && value >= 0 && value <= 1);
        values.Should().BeInAscendingOrder();
        values[0].Should().Be(0);
        values[^1].Should().Be(1);
    }

    [Fact]
    public void Overlapping_transitions_continue_from_the_current_color()
    {
        var options = new SolarCycleOptions { SunriseMinutes = 360, SunsetMinutes = 360 };
        var times = new SolarTimes(new(8, 0), new(16, 0));
        var overlapStart = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var before = SolarCycle.Evaluate(
            options,
            Day,
            Night,
            null,
            times,
            overlapStart.AddTicks(-1),
            TimeZoneInfo.Utc
        );
        var after = SolarCycle.Evaluate(
            options,
            Day,
            Night,
            null,
            times,
            overlapStart,
            TimeZoneInfo.Utc
        );
        after.Temperature.Should().BeApproximately(before.Temperature, 0.001);
        after.Brightness.Should().BeApproximately(before.Brightness, 0.00001);
    }
}
