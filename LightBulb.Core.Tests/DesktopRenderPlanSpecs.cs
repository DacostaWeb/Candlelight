using System;
using System.Collections.Generic;
using Candlelight.Engine;
using FluentAssertions;
using Xunit;

namespace Candlelight.Engine.Tests;

public class DesktopRenderPlanSpecs
{
    private static readonly DisplayDescriptor Internal = new(
        "internal",
        "Internal",
        "",
        0,
        0,
        1920,
        1080,
        true
    );
    private static readonly DisplayDescriptor Oled = new(
        "oled",
        "OLED",
        "",
        1920,
        0,
        1920,
        1080,
        false
    );

    [Fact]
    public void Warm_internal_and_red_oled_keep_the_internal_on_the_desktop_filter()
    {
        var warm = new ColorProfile(ColorMode.Temperature, 2700, 1);
        var plan = DesktopRenderPlan.Create(
            [Internal, Oled],
            new Dictionary<string, (ColorProfile, bool)>
            {
                [Internal.Id] = (warm, true),
                [Oled.Id] = (new(ColorMode.PureRed, 500, .15), true),
            },
            true
        );
        plan.DesktopGain.Should().Be(ChannelGain.FromProfile(warm));
        plan.Monitors[0].NeedsSurface.Should().BeFalse();
        plan.Monitors[1].LocalGain.Should().Be(new ChannelGain(.15, 0, 0));
        plan.At(1919, 500)!.Display.Id.Should().Be(Internal.Id);
        plan.At(1920, 500)!.Display.Id.Should().Be(Oled.Id);
        plan.At(3840, 500).Should().BeNull();
    }

    [Fact]
    public void Paused_and_unknown_monitors_keep_identity_without_channel_amplification()
    {
        var plan = DesktopRenderPlan.Create(
            [Internal, Oled],
            new Dictionary<string, (ColorProfile, bool)>
            {
                [Oled.Id] = (new(ColorMode.PureRed, 500, .15), true),
            },
            true
        );
        plan.DesktopGain.Should().Be(new ChannelGain(1, 1, 1));
        plan.Monitors[0].Enabled.Should().BeFalse();
        plan.Monitors[0].Gain.Should().Be(new ChannelGain(1, 1, 1));
        plan.Monitors[0].NeedsSurface.Should().BeFalse();
    }

    [Theory]
    [InlineData(500, .01, 12000, 1)]
    [InlineData(1800, .35, 2700, 1)]
    [InlineData(6600, .4, 4000, .5)]
    [InlineData(2700, 1, 2700, 1)]
    public void Local_and_desktop_matrices_compose_without_clipping(
        double first,
        double firstBrightness,
        double second,
        double secondBrightness
    )
    {
        var plan = DesktopRenderPlan.Create(
            [Internal, Oled],
            new Dictionary<string, (ColorProfile, bool)>
            {
                [Internal.Id] = (new(ColorMode.Temperature, first, firstBrightness), true),
                [Oled.Id] = (new(ColorMode.Temperature, second, secondBrightness), true),
            },
            true
        );
        foreach (var monitor in plan.Monitors)
        {
            var shared = plan.DesktopGain!;
            var local = monitor.LocalGain;
            (shared.Red * local.Red).Should().BeApproximately(monitor.Gain.Red, 1e-12);
            (shared.Green * local.Green).Should().BeApproximately(monitor.Gain.Green, 1e-12);
            (shared.Blue * local.Blue).Should().BeApproximately(monitor.Gain.Blue, 1e-12);
            foreach (var channel in new[] { local.Red, local.Green, local.Blue })
            {
                double.IsFinite(channel).Should().BeTrue();
                channel.Should().BeInRange(0, 1);
            }
        }
    }

    [Fact]
    public void Red_on_both_monitors_preserves_exact_zeros_and_different_brightness()
    {
        var plan = DesktopRenderPlan.Create(
            [Internal, Oled],
            new Dictionary<string, (ColorProfile, bool)>
            {
                [Internal.Id] = (new(ColorMode.PureRed, 500, .35), true),
                [Oled.Id] = (new(ColorMode.PureRed, 500, .15), true),
            },
            true
        );
        plan.DesktopGain.Should().Be(new ChannelGain(.35, 0, 0));
        plan.Monitors[0].NeedsSurface.Should().BeFalse();
        plan.Monitors[1].LocalGain.Should().Be(new ChannelGain(.15 / .35, 1, 1));
    }

    [Fact]
    public void Unavailable_desktop_api_uses_the_original_per_monitor_gain()
    {
        var plan = DesktopRenderPlan.Create(
            [Internal, Oled],
            new Dictionary<string, (ColorProfile, bool)>
            {
                [Internal.Id] = (new(ColorMode.Temperature, 2700, 1), true),
                [Oled.Id] = (new(ColorMode.PureRed, 500, .15), true),
            },
            false
        );
        plan.DesktopGain.Should().BeNull();
        foreach (var monitor in plan.Monitors)
            monitor.LocalGain.Should().Be(monitor.Gain);
    }
}
