using System;
using System.Runtime.InteropServices;
using Candlelight.Engine;
using FluentAssertions;
using Xunit;
using DailySchedule = Candlelight.Engine.DailySchedule;

namespace Candlelight.Engine.Tests;

public class NextEngineSpecs
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.15)]
    [InlineData(1)]
    public void Red_has_exact_zero_green_and_blue(double brightness)
    {
        var gain = ChannelGain.FromProfile(new(ColorMode.PureRed, 500, brightness));
        gain.Should().Be(new ChannelGain(brightness, 0, 0));
        var matrix = Native.ColorEffect.FromGain(gain);
        matrix.Values[0].Should().Be((float)brightness);
        matrix.Values[6].Should().Be(0);
        matrix.Values[12].Should().Be(0);
        matrix.Values[18].Should().Be(1);
        matrix.Values[24].Should().Be(1);
        Marshal.SizeOf<Native.ColorEffect>().Should().Be(100);
    }

    [Fact]
    public void Normal_is_identity_and_brightness_scales_all_channels()
    {
        ChannelGain.FromProfile(ColorProfile.Normal).Should().Be(new ChannelGain(1, 1, 1));
        ChannelGain
            .FromProfile(ColorProfile.Normal with { Brightness = 0.15 })
            .Should()
            .Be(new ChannelGain(0.15, 0.15, 0.15));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1800)]
    [InlineData(2700)]
    [InlineData(4000)]
    [InlineData(6600)]
    [InlineData(12000)]
    public void Temperature_outputs_stay_finite_and_within_brightness(double temperature)
    {
        var gain = ChannelGain.FromProfile(new(ColorMode.Temperature, temperature, 0.4));
        foreach (var channel in new[] { gain.Red, gain.Green, gain.Blue })
        {
            double.IsFinite(channel).Should().BeTrue();
            channel.Should().BeInRange(0, 0.4);
        }
    }

    [Fact]
    public void Rejects_nonfinite_values_before_native_calls()
    {
        var action = () => ChannelGain.FromProfile(new(ColorMode.Temperature, double.NaN, 1));
        action.Should().Throw<ArgumentException>();
        action = () =>
            ChannelGain.FromProfile(new(ColorMode.PureRed, 500, double.PositiveInfinity));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Schedule_repeats_across_midnight()
    {
        SchedulePoint[] points =
        [
            new(new(7, 0), ColorProfile.Normal),
            new(new(22, 0), new(ColorMode.PureRed, 500, 0.15)),
        ];
        DailySchedule.Evaluate(points, new(1, 0)).Mode.Should().Be(ColorMode.PureRed);
        DailySchedule.Evaluate(points, new(7, 0)).Should().Be(ColorProfile.Normal);
        DailySchedule.Evaluate(points, new(22, 0)).Brightness.Should().Be(0.15);
    }

    [Fact]
    public void Transition_shortens_to_the_next_point()
    {
        SchedulePoint[] points =
        [
            new(new(19, 0), new(ColorMode.Temperature, 4000, 1)),
            new(new(20, 0), new(ColorMode.Temperature, 2000, 0.5), 120),
            new(new(21, 0), new(ColorMode.Temperature, 2000, 0.5)),
        ];
        DailySchedule
            .Evaluate(points, new(20, 30))
            .Should()
            .Be(new ColorProfile(ColorMode.Temperature, 3000, 0.75));
        DailySchedule.Evaluate(points, new(21, 0)).Should().Be(points[2].Profile);
    }

    [Fact]
    public void Entering_red_does_not_fade_through_green_or_blue()
    {
        SchedulePoint[] points =
        [
            new(new(7, 0), ColorProfile.Normal),
            new(new(22, 0), new(ColorMode.PureRed, 500, 0.15), 30),
        ];
        var output = DailySchedule.Evaluate(points, new(22, 0));
        ChannelGain.FromProfile(output).Should().Be(new ChannelGain(0.15, 0, 0));
    }

    [Fact]
    public void Single_point_is_constant_and_duplicate_times_are_rejected()
    {
        var point = new SchedulePoint(new(22, 0), new(ColorMode.Temperature, 2700, 1), 30);
        DailySchedule.Evaluate([point], new(22, 0)).Should().Be(point.Profile);
        var action = () => DailySchedule.Evaluate([point, point], new(22, 0));
        action.Should().Throw<ArgumentException>();
    }
}
