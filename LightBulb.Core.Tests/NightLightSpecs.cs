using System;
using System.Linq;
using LightBulb.PlatformInterop;
using Xunit;

namespace LightBulb.Core.Tests;

public class NightLightSpecs
{
    private static readonly byte[] Active = Convert.FromHexString(
        "434201000A0201002A0680A4A7DA062A2B0E15434201001000D00A02C61487AD99DAE698E9EE0100000000"
    );

    [Fact]
    public void Known_active_and_inactive_states_are_decoded()
    {
        Assert.True(NightLight.DecodeActive(Active));
        var inactive = Convert.FromHexString(
            "434201000A0201002A06C0C1A2D6062A2B0E1343420100D00A02C61487E28AD1D3F8D5EE0100000000"
        );
        Assert.False(NightLight.DecodeActive(inactive));
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
}
