using System;
using System.Linq;
using LightBulb.PlatformInterop;
using Xunit;

namespace LightBulb.Core.Tests;

public class NightLightSpecs
{
    private static readonly byte[] Inactive = Convert.FromHexString(
        "434201000A0201002A06C0C1A2D6062A2B0E1343420100D00A02C61487E28AD1D3F8D5EE0100000000"
    );
    private static readonly byte[] Active = Convert.FromHexString(
        "434201000A0201002A0680A4A7DA062A2B0E15434201001000D00A02C61487AD99DAE698E9EE0100000000"
    );

    [Fact]
    public void Known_active_and_inactive_states_are_decoded()
    {
        Assert.True(NightLight.DecodeActive(Active));
        Assert.False(NightLight.DecodeActive(Inactive));
    }

    [Fact]
    public void Truncations_and_unknown_schemas_do_not_claim_night_light_protection()
    {
        for (var length = 0; length < Active.Length; length++)
            Assert.Null(NightLight.DecodeActive(Active.Take(length).ToArray()));
        var changed = Active.ToArray();
        changed[2] = 2;
        Assert.Null(NightLight.DecodeActive(changed));
        changed = Active.ToArray();
        changed[24] = 1; // Unknown running-status value.
        Assert.Null(NightLight.DecodeActive(changed));
        Assert.Null(NightLight.DecodeActive(Active.Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void State_switches_use_new_timestamps_and_preserve_usable_flags()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000).AddTicks(1234567);
        var enabled = NightLight.RewriteActive(Inactive, true, now);
        var expected = Convert.FromHexString(
            "434201000A0201002A0680A4A7DA062A2B0E15434201001000D00A02C61487AD99DAE698E9EE0100000000"
        );
        Assert.Equal(expected, enabled);
        var disabled = NightLight.RewriteActive(enabled!, false, now.AddSeconds(-100));
        Assert.False(NightLight.DecodeActive(disabled!));
        // A clock behind the previous stamp still advances it by two seconds.
        Assert.Contains("82A4A7DA06", Convert.ToHexString(disabled!));
        Assert.Same(disabled, NightLight.RewriteActive(disabled!, false, now));

        var unusable = Active[..^4].Concat(new byte[] { 0xC2, 30, 0, 0, 0, 0, 0 }).ToArray();
        unusable[18] += 3;
        Assert.False(NightLight.DecodeActive(unusable));
        Assert.Null(NightLight.RewriteActive(unusable, false, now));
    }

    [Fact]
    public void Unknown_or_corrupt_state_is_never_rewritten()
    {
        var now = DateTimeOffset.UtcNow;
        for (var length = 0; length < Active.Length; length++)
            Assert.Null(NightLight.RewriteActive(Active.Take(length).ToArray(), false, now));
        var unknown = Active.ToArray();
        unknown[23] = 0xA2;
        Assert.Null(NightLight.RewriteActive(unknown, false, now));
    }
}
