using System;
using System.Collections.Generic;
using LightBulb.PlatformInterop;
using LightBulb.Services;
using Xunit;

namespace LightBulb.Core.Tests;

public class GammaServiceSpecs
{
    private sealed class Device : IGammaDevice
    {
        public List<GammaColor> Writes { get; } = [];
        public int Resets { get; private set; }
        public bool Succeeds { get; set; } = true;

        public bool SetGamma(double red, double green, double blue)
        {
            Writes.Add(new(red, green, blue));
            return Succeeds;
        }

        public void ResetGamma() => Resets++;

        public void Dispose() { }
    }

    private static GammaService.DisplayContext Context(string id, Device device) =>
        new(new DisplayInfo(id, id, id, false, new Rect(0, 0, 1920, 1080)), device);

    [Fact]
    public void Gamma_is_routed_independently_to_each_monitor()
    {
        var internalDisplay = new Device();
        var oled = new Device();
        using var service = new GammaService(
            new SettingsService(),
            () => [Context("internal", internalDisplay), Context("oled", oled)],
            () => 10000,
            false
        );
        service.SetGamma(
            ColorConfiguration.Default,
            new Dictionary<string, ColorConfiguration>
            {
                ["internal"] = new(6600, 1),
                ["oled"] = new(500, 0.15),
            }
        );
        Assert.Equal(new GammaColor(0.15, 0, 0), Assert.Single(oled.Writes));
        Assert.Equal(1, Assert.Single(internalDisplay.Writes).Red);
        Assert.True(internalDisplay.Writes[0].Blue > 0);
    }

    [Fact]
    public void Wake_recomputes_the_target_before_any_gamma_is_written()
    {
        var device = new Device();
        using var service = new GammaService(
            new SettingsService(),
            () => [Context("oled", device)],
            () => 10000,
            false
        );
        service.SetGamma(new(6600, 1));
        device.Writes.Clear();
        service.RecoveryRequested += () => service.SetGamma(new(500, 0.15));
        service.Recover();
        Assert.NotEmpty(device.Writes);
        Assert.All(device.Writes, color => Assert.Equal(new GammaColor(0.15, 0, 0), color));
        Assert.Equal(0, device.Resets);
    }

    [Fact]
    public void Recovery_retries_end_after_five_seconds_and_stop_when_display_turns_off()
    {
        long now = 10000;
        var device = new Device();
        using var service = new GammaService(
            new SettingsService(),
            () => [Context("oled", device)],
            () => now,
            false
        );
        service.SetGamma(new(500, 0.15));
        service.Recover();
        var count = device.Writes.Count;
        now += 50;
        service.RecoveryTick();
        Assert.Equal(count + 1, device.Writes.Count);
        now = 15000;
        service.RecoveryTick();
        Assert.Equal(count + 1, device.Writes.Count);
        service.Recover();
        service.OnDisplayState(0);
        count = device.Writes.Count;
        service.RecoveryTick();
        service.SetGamma(new(2700, 0.5));
        Assert.Equal(count, device.Writes.Count);
    }

    [Fact]
    public void Polling_reapplies_unchanged_gamma_without_being_starved_by_frequent_ui_ticks()
    {
        long now = 10000;
        var device = new Device();
        using var service = new GammaService(
            new SettingsService { IsGammaPollingEnabled = true },
            () => [Context("oled", device)],
            () => now,
            false
        );
        service.SetGamma(new(500, 0.15));
        for (var i = 0; i < 20; i++)
        {
            now += 50;
            service.SetGamma(new(500, 0.15));
        }
        Assert.Equal(2, device.Writes.Count);
    }

    [Fact]
    public void A_driver_failure_is_reported_and_retried()
    {
        var device = new Device { Succeeds = false };
        using var service = new GammaService(
            new SettingsService(),
            () => [Context("oled", device)],
            () => 10000,
            false
        );
        service.SetGamma(new(500, 0.15));
        Assert.Contains("oled", service.FailedDisplayIds);
        device.Succeeds = true;
        service.SetGamma(new(500, 0.15));
        Assert.Empty(service.FailedDisplayIds);
        Assert.Equal(2, device.Writes.Count);
    }
}
