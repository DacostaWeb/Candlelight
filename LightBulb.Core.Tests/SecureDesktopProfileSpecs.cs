using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Candlelight.Engine;
using FluentAssertions;
using Xunit;

namespace Candlelight.Engine.Tests;

public class SecureDesktopProfileSpecs
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Inactive_preparation_cannot_write_shared_desktop_or_system_cursor_state(
        bool desktop,
        bool cursor
    )
    {
        Action create = () =>
            new MagnificationEngine(
                allowDesktopEffect: desktop,
                manageSystemCursors: cursor,
                keepInactiveLocalSurfaces: true
            );
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Windows_session_identity_matches_the_current_user()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        SessionAccount
            .UserSid(System.Diagnostics.Process.GetCurrentProcess().SessionId)
            .Should()
            .Be(identity.User!.Value);
    }

    private static SecureMonitorProfile Oled =>
        new("edid:OLED", true, new(ColorMode.PureRed, 500, .15), []);

    [Fact]
    public void Transfer_preserves_distinct_monitors_and_schedules_across_midnight()
    {
        SecureMonitorProfile[] profiles =
        [
            Oled,
            new(
                "edid:internal",
                true,
                new(ColorMode.Temperature, 2700, 1),
                [
                    new(new(7, 0), ColorProfile.Normal),
                    new(new(22, 0), new(ColorMode.Temperature, 1800, 1)),
                ]
            ),
        ];
        var copy = SecureDesktopProfiles.Decode(SecureDesktopProfiles.Encode(profiles));
        copy[0].Evaluate(new(1, 0)).Should().Be(Oled.Manual);
        copy[1].Evaluate(new(1, 0)).Temperature.Should().Be(1800);
        copy[1].Evaluate(new(8, 0)).Should().Be(ColorProfile.Normal);
        ChannelGain.FromProfile(copy[0].Manual).Should().Be(new ChannelGain(.15, 0, 0));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Schema\":2,\"Monitors\":[]}")]
    [InlineData("{\"Schema\":1,\"Monitors\":null}")]
    [InlineData("{\"Schema\":1,\"Monitors\":[null]}")]
    [InlineData("{\"Schema\":1,\"Monitors\":[],\"Command\":\"anything\"}")]
    public void Untrusted_transfer_rejects_null_unknown_schemas_and_commands(string value)
    {
        Action decode = () => SecureDesktopProfiles.Decode(Encoding.UTF8.GetBytes(value));
        decode.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(-.1, 500, 1)]
    [InlineData(0, 500, 1)]
    [InlineData(1.1, 500, 1)]
    [InlineData(.15, 499, 1)]
    [InlineData(.15, 12001, 1)]
    [InlineData(.15, 500, 100)]
    public void Untrusted_color_values_are_rejected_instead_of_clamped(
        double brightness,
        double temperature,
        int mode
    )
    {
        var monitor = Oled with { Manual = new((ColorMode)mode, temperature, brightness) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Monitors = new[] { monitor } }
        );
        Action decode = () => SecureDesktopProfiles.Decode(bytes);
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Size_duplicate_identity_and_schedule_limits_are_enforced()
    {
        Action decode = () =>
            SecureDesktopProfiles.Decode(new byte[SecureDesktopProfiles.MaximumBytes + 1]);
        decode.Should().Throw<InvalidDataException>();
        Action encode = () => SecureDesktopProfiles.Encode([Oled, Oled]);
        encode.Should().Throw<InvalidDataException>();
        encode = () => SecureDesktopProfiles.Encode([Oled with { Id = new string('x', 257) }]);
        encode.Should().Throw<InvalidDataException>();
        encode = () =>
            SecureDesktopProfiles.Encode([
                Oled with
                {
                    Schedule = [new(new(22, 0), Oled.Manual), new(new(22, 0), Oled.Manual)],
                },
            ]);
        encode.Should().Throw<ArgumentException>();
    }
}
