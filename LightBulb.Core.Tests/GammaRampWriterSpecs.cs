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
        public GammaRamp Gdi { get; private set; } = GammaRamp.Identity();
        public GammaRamp Color { get; private set; } = GammaRamp.Identity();
        public List<string> Writes { get; } = [];

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
            return true;
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
            return true;
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
    public void Readback_never_accepts_nonzero_green_or_blue_for_red_only()
    {
        var expected = GammaRamp.Create(0.15, 0, 0, 0);
        var actual = GammaRamp.Create(0.15, 0, 0, 0);
        actual.Green[255] = 1;
        Assert.False(expected.Matches(actual));
    }
}
