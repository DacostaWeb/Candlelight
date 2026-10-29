using System;
using System.IO;
using LightBulb.Services;
using Xunit;

namespace LightBulb.Core.Tests;

public class WakeSettingsSpecs
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Upgrade_disables_old_defaults_but_preserves_later_explicit_choices(
        int version,
        bool expectedEnabled
    )
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "candlelight-settings-" + Guid.NewGuid() + ".json"
        );
        File.WriteAllText(
            path,
            $$"""
            {
              "WakeSettingsVersion": {{version}},
              "IsWakeRecoveryEnabled": true,
              "IsNightLightProtectionEnabled": true,
              "SelectedDisplayId": "oled",
              "DisplayProfiles": [{
                "Id": "oled", "Name": "OLED",
                "NightConfiguration": { "Temperature": 500, "Brightness": 0.25 }
              }]
            }
            """
        );
        try
        {
            var settings = new SettingsService(path);
            Assert.True(settings.Load());
            Assert.Equal(1, settings.WakeSettingsVersion);
            Assert.Equal(expectedEnabled, settings.IsWakeRecoveryEnabled);
            Assert.Equal(expectedEnabled, settings.IsNightLightProtectionEnabled);
            Assert.Equal("oled", settings.SelectedDisplayId);
            var profile = Assert.Single(settings.DisplayProfiles);
            Assert.Equal(500, profile.NightConfiguration.Temperature);
            Assert.Equal(0.25, profile.NightConfiguration.Brightness);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
