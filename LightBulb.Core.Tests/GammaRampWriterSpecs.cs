using System.Collections.Generic;
using LightBulb.PlatformInterop.Internal;
using Xunit;

namespace LightBulb.Core.Tests;

public class GammaRampWriterSpecs
{
    private sealed class Api : IGammaRampApi
    {
        public bool IsColorSystemAvailable { get; set; } = true;
        public bool RejectGdiTarget { get; set; }
        public bool RejectGdiIdentity { get; set; }
        public bool IgnoreGdiWrite { get; set; }
        public bool IgnoreColorWrite { get; set; }
        public bool RejectColorWrite { get; set; }
        public bool CanReadColor { get; set; } = true;
        public bool CanReadGdi { get; set; } = true;
        public GammaRamp Gdi { get; private set; } = GammaRamp.Identity();
        public GammaRamp Color { get; private set; } = GammaRamp.Identity();
        public List<string> Writes { get; } = [];

        public void ChangeColor(GammaRamp ramp) => Color = ramp;

        public void ChangeGdi(GammaRamp ramp) => Gdi = ramp;

        public bool WriteGdi(ref GammaRamp ramp)
        {
            Writes.Add("gdi");
            if (RejectGdiTarget && !GammaRamp.Identity().Matches(ramp))
                return false;
            if (RejectGdiIdentity && GammaRamp.Identity().Matches(ramp))
                return false;
            if (!IgnoreGdiWrite)
                Gdi = ramp;
            return true;
        }

        public bool ReadGdi(out GammaRamp ramp)
        {
            ramp = Gdi;
            return CanReadGdi;
        }

        public bool WriteColorSystem(ref GammaRamp ramp)
        {
            Writes.Add("color");
            if (RejectColorWrite)
                return false;
            if (!IgnoreColorWrite)
                Color = ramp;
            return true;
        }

        public bool ReadColorSystem(out GammaRamp ramp)
        {
            ramp = Color;
            return CanReadColor;
        }
    }

    [Fact]
    public void Accepted_gdi_ramps_do_not_touch_the_color_system()
    {
        var api = new Api();
        var writer = new GammaRampWriter(api);
        Assert.True(writer.Apply(GammaRamp.Create(1, 0.8, 0.6, 0)));
        Assert.False(writer.UsesColorSystem);
        Assert.Equal(["gdi"], api.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rejected_or_silently_ignored_red_ramps_use_verified_color_system_output(bool silent)
    {
        var api = new Api { RejectGdiTarget = !silent, IgnoreGdiWrite = silent };
        var writer = new GammaRampWriter(api);
        var red = GammaRamp.Create(0.15, 0, 0, 1);
        Assert.True(writer.Apply(red));
        Assert.True(writer.UsesColorSystem);
        Assert.True(red.Matches(api.Color));
        Assert.All(api.Color.Green, value => Assert.Equal((ushort)0, value));
        Assert.All(api.Color.Blue, value => Assert.Equal((ushort)0, value));
        Assert.Equal(["gdi", "color"], api.Writes);
    }

    [Fact]
    public void A_later_daytime_value_clears_the_red_color_layer()
    {
        var api = new Api { RejectGdiTarget = true };
        var writer = new GammaRampWriter(api);
        Assert.True(writer.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        Assert.True(writer.Apply(GammaRamp.Identity()));
        Assert.True(GammaRamp.Identity().Matches(api.Color));
        Assert.True(GammaRamp.Identity().Matches(api.Gdi));
    }

    [Fact]
    public void A_color_layer_left_by_another_process_cannot_mask_later_gdi_changes()
    {
        var api = new Api();
        var leftover = GammaRamp.Create(0.15, 0, 0, 0);
        api.WriteColorSystem(ref leftover);
        api.Writes.Clear();
        var writer = new GammaRampWriter(api);
        Assert.True(writer.Apply(GammaRamp.Identity()));
        Assert.True(writer.UsesColorSystem);
        Assert.Equal(["color"], api.Writes);
        Assert.True(GammaRamp.Identity().Matches(api.Color));
    }

    [Fact]
    public void A_recreated_context_keeps_ownership_of_the_color_layer()
    {
        var api = new Api { RejectGdiTarget = true };
        var first = new GammaRampWriter(api);
        Assert.True(first.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        var recreated = new GammaRampWriter(api, first.UsesColorSystem);
        Assert.True(recreated.Apply(GammaRamp.Identity()));
        Assert.True(GammaRamp.Identity().Matches(api.Color));
    }

    [Fact]
    public void Wake_retries_do_not_rewrite_an_already_neutral_gdi_layer()
    {
        var api = new Api { RejectGdiIdentity = true };
        var writer = new GammaRampWriter(api, true);
        for (var i = 0; i < 100; i++)
            Assert.True(writer.Apply(GammaRamp.Create(0.15, 0, 0, i % 5)));
        Assert.Equal(100, api.Writes.Count);
        Assert.All(api.Writes, operation => Assert.Equal("color", operation));
        Assert.Contains("GDI identity kept", writer.LastOperation);
    }

    [Fact]
    public void A_matching_gdi_ramp_is_verified_without_a_write()
    {
        var api = new Api();
        var warm = GammaRamp.Create(1, 0.8, 0.6, 0);
        api.WriteGdi(ref warm);
        api.Writes.Clear();
        var writer = new GammaRampWriter(api);
        Assert.True(writer.IsCurrent(warm));
        Assert.False(writer.DidWrite);
        Assert.False(writer.UsesColorSystem);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public void A_surviving_color_filter_is_verified_without_reprogramming_either_layer()
    {
        var api = new Api();
        var red = GammaRamp.Create(0.15, 0, 0, 0);
        api.WriteColorSystem(ref red);
        api.Writes.Clear();
        var writer = new GammaRampWriter(api);
        for (var i = 0; i < 100; i++)
            Assert.True(writer.IsCurrent(red));
        Assert.Empty(api.Writes);
        Assert.False(writer.DidWrite);
        Assert.True(writer.UsesColorSystem);
        // Verifying a preexisting filter still takes responsibility for exit reset.
        writer.Reset();
        Assert.True(GammaRamp.Identity().Matches(api.Color));
    }

    [Fact]
    public void Matching_color_with_an_extra_gdi_filter_requires_repair()
    {
        var api = new Api();
        var red = GammaRamp.Create(0.15, 0, 0, 0);
        var extraGamma = GammaRamp.Create(1, 0.8, 0.6, 0);
        api.WriteColorSystem(ref red);
        api.WriteGdi(ref extraGamma);
        api.Writes.Clear();
        var writer = new GammaRampWriter(api);
        Assert.False(writer.IsCurrent(red));
        Assert.Empty(api.Writes);
        Assert.True(writer.Apply(red));
        Assert.Equal(["color", "gdi"], api.Writes);
    }

    [Fact]
    public void Matching_gdi_cannot_hide_a_different_color_filter()
    {
        var api = new Api();
        var warm = GammaRamp.Create(1, 0.8, 0.6, 0);
        var red = GammaRamp.Create(0.15, 0, 0, 0);
        api.WriteGdi(ref warm);
        api.WriteColorSystem(ref red);
        var writer = new GammaRampWriter(api);
        Assert.False(writer.IsCurrent(warm));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void An_unreadable_layer_cannot_be_assumed_correct(bool readColor, bool readGdi)
    {
        var api = new Api();
        var red = GammaRamp.Create(0.15, 0, 0, 0);
        api.WriteColorSystem(ref red);
        api.CanReadColor = readColor;
        api.CanReadGdi = readGdi;
        var writer = new GammaRampWriter(api, true);
        Assert.False(writer.IsCurrent(red));
    }

    [Fact]
    public void Red_only_verification_detects_even_one_nonzero_blue_entry()
    {
        var api = new Api();
        var expected = GammaRamp.Create(0.15, 0, 0, 0);
        var actual = GammaRamp.Create(0.15, 0, 0, 0);
        actual.Blue[100] = 1;
        api.WriteColorSystem(ref actual);
        var writer = new GammaRampWriter(api);
        Assert.False(writer.IsCurrent(expected));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void An_unreadable_output_is_not_written_through_either_backend(
        bool readColor,
        bool readGdi
    )
    {
        var api = new Api { CanReadColor = readColor, CanReadGdi = readGdi };
        var writer = new GammaRampWriter(api);
        Assert.False(writer.Apply(GammaRamp.Create(0.25, 0, 0, 0)));
        Assert.Empty(api.Writes);
        Assert.False(writer.DidWrite);
        Assert.NotNull(writer.FailureReason);
    }

    [Fact]
    public void Systems_without_the_internal_color_api_can_still_use_readable_gdi()
    {
        var api = new Api { IsColorSystemAvailable = false, CanReadColor = false };
        var writer = new GammaRampWriter(api);
        Assert.True(writer.Apply(GammaRamp.Create(1, 0.8, 0.6, 0)));
        Assert.Equal(["gdi"], api.Writes);
    }

    [Fact]
    public void A_non_neutral_gdi_layer_is_cleared_after_the_color_filter_is_installed()
    {
        var api = new Api();
        var oldGamma = GammaRamp.Create(1, 0.8, 0.6, 0);
        api.WriteGdi(ref oldGamma);
        api.Writes.Clear();
        var writer = new GammaRampWriter(api, true);
        var red = GammaRamp.Create(0.15, 0, 0, 0);
        Assert.True(writer.Apply(red));
        Assert.Equal(["color", "gdi"], api.Writes);
        Assert.True(GammaRamp.Identity().Matches(api.Gdi));
        Assert.True(red.Matches(api.Color));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_failed_or_ignored_gdi_neutralization_is_not_reported_as_success(bool silent)
    {
        var api = new Api();
        var oldGamma = GammaRamp.Create(1, 0.8, 0.6, 0);
        api.WriteGdi(ref oldGamma);
        api.IgnoreGdiWrite = silent;
        api.RejectGdiIdentity = !silent;
        var writer = new GammaRampWriter(api, true);
        Assert.False(writer.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        Assert.Contains("neutralizar", writer.FailureReason);
    }

    [Fact]
    public void Exit_resets_both_layers_after_color_system_was_used()
    {
        var api = new Api { RejectGdiTarget = true };
        var writer = new GammaRampWriter(api);
        Assert.True(writer.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        api.Writes.Clear();
        writer.Reset();
        Assert.Equal(["color", "gdi"], api.Writes);
        Assert.True(GammaRamp.Identity().Matches(api.Color));
        Assert.True(GammaRamp.Identity().Matches(api.Gdi));
    }

    [Fact]
    public void An_unavailable_backend_reports_failure_instead_of_success()
    {
        var api = new Api { RejectGdiTarget = true, IsColorSystemAvailable = false };
        var writer = new GammaRampWriter(api);
        Assert.False(writer.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        Assert.Contains("reiniciar", writer.FailureReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_or_ignored_color_system_writes_are_not_reported_as_applied(bool silent)
    {
        var api = new Api
        {
            RejectGdiTarget = true,
            RejectColorWrite = !silent,
            IgnoreColorWrite = silent,
        };
        var writer = new GammaRampWriter(api);
        Assert.False(writer.Apply(GammaRamp.Create(0.15, 0, 0, 0)));
        Assert.NotNull(writer.FailureReason);
        if (silent)
            Assert.True(writer.UsesColorSystem);
    }

    [Fact]
    public void Successful_writes_replaced_by_a_windows_fade_wait_without_writing_and_resume_once()
    {
        long now = 0;
        var api = new Api();
        var writer = new GammaRampWriter(api, true, new GammaRampGuard(() => now));
        Assert.True(writer.Apply(GammaRamp.Create(1, 0.65, 0.3, 0)));
        api.Writes.Clear();

        for (var tick = 0; tick < 2400; tick++)
        {
            now += 50;
            api.ChangeColor(GammaRamp.Create(1, 0.7 + tick * 0.0001, 0.5, 0));
            Assert.False(writer.Apply(GammaRamp.Create(1, 0.6, 0.2, 0)));
            Assert.True(writer.IsDeferred);
            Assert.False(writer.DidWrite);
            Assert.Null(writer.FailureReason);
            Assert.Empty(api.Writes);
        }

        now += 2999;
        Assert.False(writer.Apply(GammaRamp.Create(1, 0.8, 0.6, 0)));
        Assert.Empty(api.Writes);
        now++;
        var latest = GammaRamp.Create(1, 0.85, 0.65, 0);
        Assert.True(writer.Apply(latest));
        Assert.False(writer.IsDeferred);
        Assert.True(latest.Matches(api.Color));
        Assert.Equal(["color"], api.Writes);
    }

    [Fact]
    public void A_fade_crossing_the_requested_target_does_not_end_the_quiet_interval()
    {
        long now = 0;
        var api = new Api();
        var writer = new GammaRampWriter(api, true, new GammaRampGuard(() => now));
        var requested = GammaRamp.Create(1, 0.8, 0.6, 0);
        Assert.True(writer.Apply(requested));
        api.Writes.Clear();
        api.ChangeColor(GammaRamp.Create(1, 0.9, 0.7, 0));
        Assert.False(writer.Apply(requested));
        now = 1000;
        api.ChangeColor(requested);
        Assert.False(writer.IsCurrent(requested));
        Assert.False(writer.Apply(requested));
        Assert.True(writer.IsDeferred);
        now = 2000;
        api.ChangeColor(GammaRamp.Create(1, 0.7, 0.5, 0));
        Assert.False(writer.Apply(requested));
        Assert.Empty(api.Writes);
        now = 4999;
        Assert.False(writer.Apply(requested));
        now++;
        Assert.True(writer.Apply(requested));
        Assert.Equal(["color"], api.Writes);
    }

    [Fact]
    public void Recreated_contexts_keep_waiting_and_do_not_restart_the_fight()
    {
        long now = 0;
        var api = new Api();
        var guard = new GammaRampGuard(() => now);
        var target = GammaRamp.Create(1, 0.65, 0.3, 0);
        Assert.True(new GammaRampWriter(api, true, guard).Apply(target));
        api.Writes.Clear();
        for (var i = 0; i < 100; i++)
        {
            now += 100;
            api.ChangeColor(GammaRamp.Create(1, 0.9, 0.7 + i * 0.001, 0));
            var recreated = new GammaRampWriter(api, true, guard);
            Assert.False(recreated.IsCurrent(target));
            Assert.False(recreated.Apply(target));
            Assert.True(recreated.IsDeferred);
        }
        Assert.Empty(api.Writes);
        now += 3000;
        Assert.True(new GammaRampWriter(api, true, guard).Apply(target));
        Assert.Equal(["color"], api.Writes);
    }

    [Fact]
    public void An_external_gdi_change_is_also_deferred_and_exit_does_not_reset_it()
    {
        var api = new Api();
        var writer = new GammaRampWriter(api);
        var target = GammaRamp.Create(1, 0.8, 0.6, 0);
        Assert.True(writer.Apply(target));
        api.Writes.Clear();
        api.ChangeGdi(GammaRamp.Identity());
        Assert.False(writer.Apply(target));
        Assert.True(writer.IsDeferred);
        writer.Reset();
        Assert.Empty(api.Writes);
    }

    [Fact]
    public void Normal_profile_changes_are_not_mistaken_for_external_changes()
    {
        var api = new Api();
        var writer = new GammaRampWriter(api, true);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(writer.Apply(GammaRamp.Create(1, 0.6 + i * 0.001, 0.3, i % 5)));
            Assert.False(writer.IsDeferred);
        }
        Assert.Equal(100, api.Writes.Count);
    }

    [Fact]
    public void A_real_write_failure_after_the_quiet_interval_is_not_reported_as_deferral()
    {
        long now = 0;
        var api = new Api();
        var writer = new GammaRampWriter(api, true, new GammaRampGuard(() => now));
        var target = GammaRamp.Create(1, 0.65, 0.3, 0);
        Assert.True(writer.Apply(target));
        api.ChangeColor(GammaRamp.Identity());
        Assert.False(writer.Apply(target));
        now = 3000;
        api.RejectColorWrite = true;
        Assert.False(writer.Apply(target));
        Assert.False(writer.IsDeferred);
        Assert.NotNull(writer.FailureReason);
    }

    [Fact]
    public void Readback_never_accepts_nonzero_green_or_blue_for_red_only()
    {
        var expected = GammaRamp.Create(0.15, 0, 0, 0);
        var actual = GammaRamp.Create(0.15, 0, 0, 0);
        actual.Green[255] = 1;
        Assert.False(expected.Matches(actual));
    }
}
