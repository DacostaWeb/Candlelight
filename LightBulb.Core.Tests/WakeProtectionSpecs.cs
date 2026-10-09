using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LightBulb.PlatformInterop;
using LightBulb.Services;
using Xunit;

namespace LightBulb.Core.Tests;

public class WakeProtectionSpecs
{
    private sealed class Light(List<string> events) : INightLightControl
    {
        public bool Active { get; set; }
        public bool RejectOff { get; set; }

        public bool? ReadActive() => Active;

        public bool SetActive(bool active)
        {
            events.Add(active ? "light:on" : "light:off");
            if (!active && RejectOff)
                return false;
            Active = active;
            return true;
        }
    }

    private sealed class Cover(List<string> events) : IBlackoutCover
    {
        public TaskCompletionSource? Paint { get; set; }
        public bool Visible { get; private set; }

        public void Prepare()
        {
            Visible = true;
            events.Add("cover:prepare");
        }

        public async Task ShowAsync(CancellationToken token)
        {
            Visible = true;
            events.Add("cover:shown");
            await Task.Yield();
            if (Paint is not null)
                await Paint.Task.WaitAsync(token);
        }

        public void Hide()
        {
            Visible = false;
            events.Add("cover:hidden");
        }

        public void Dispose() => Hide();
    }

    private sealed class Device(List<string> events) : IGammaDevice
    {
        public bool Succeeds { get; set; } = true;
        public List<GammaColor> Writes { get; } = [];

        public bool SetGamma(double red, double green, double blue)
        {
            events.Add("gamma:write");
            Writes.Add(new(red, green, blue));
            return Succeeds;
        }

        public void ResetGamma() => events.Add("gamma:reset");

        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        public List<string> Events { get; } = [];
        public SettingsService Settings { get; } = new();
        public Light Light { get; }
        public Cover Cover { get; }
        public Device Device { get; }
        public GammaService Gamma { get; }
        public WakeProtectionService Protection { get; }
        public long Now { get; set; }
        public int EmergencyExits { get; private set; }

        public Fixture(string? statePath = null, bool enabled = true, bool initiallyOn = false)
        {
            Settings.IsNightLightProtectionEnabled = enabled;
            Light = new(Events) { Active = initiallyOn };
            Cover = new(Events);
            Device = new(Events);
            Gamma = new(
                Settings,
                () =>
                    [
                        new(
                            new(
                                "internal",
                                "internal",
                                "internal",
                                true,
                                new Rect(0, 0, 1920, 1080)
                            ),
                            Device
                        ),
                    ],
                () => Now,
                false
            );
            Gamma.SetGamma(new(2700, 1));
            Protection = new(
                Settings,
                Gamma,
                Light,
                Cover,
                (duration, _) =>
                {
                    Events.Add("wait:" + duration.TotalMilliseconds);
                    return Task.CompletedTask;
                },
                statePath,
                () => EmergencyExits++,
                clock: () => Now
            );
        }

        public void Dispose()
        {
            Gamma.Dispose();
            Protection.Dispose();
        }
    }

    [Fact]
    public async Task Sleep_arms_synchronously_and_wake_turns_off_only_behind_the_cover()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Events.Clear();
        f.Device.Writes.Clear();
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(0);
        Assert.True(f.Light.Active);
        Assert.Equal(new[] { "cover:prepare", "light:on" }, f.Events);
        f.Gamma.SetGamma(new(4000, 1));
        f.Gamma.WatchdogTick();
        Assert.Empty(f.Device.Writes);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.False(f.Light.Active);
        Assert.False(f.Cover.Visible);
        Assert.Equal(
            new[]
            {
                "cover:prepare",
                "light:on",
                "cover:shown",
                "light:off",
                "wait:900",
                "gamma:write",
                "wait:250",
                "gamma:write",
                "cover:hidden",
            },
            f.Events
        );
        Assert.All(
            f.Device.Writes,
            color => Assert.Equal(GammaColor.FromConfiguration(new(4000, 1)), color)
        );
    }

    [Fact]
    public async Task Duplicate_resume_notifications_do_not_start_another_handover()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Cover.Paint = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        var pending = f.Protection.PendingTransition;
        f.Gamma.OnDisplayState(1);
        Assert.Same(pending, f.Protection.PendingTransition);
        Assert.Single(f.Events, e => e == "cover:shown");
        Assert.True(f.Light.Active);
        f.Cover.Paint.SetResult();
        await pending;
        Assert.False(f.Light.Active);
        Assert.False(f.Cover.Visible);
    }

    [Fact]
    public async Task A_new_sleep_cancels_the_old_transition_without_uncovering()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Cover.Paint = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        var pending = f.Protection.PendingTransition;
        f.Gamma.OnDisplayState(0);
        await pending;
        Assert.True(f.Light.Active);
        Assert.True(f.Cover.Visible);
        Assert.DoesNotContain("cover:hidden", f.Events);
        Assert.DoesNotContain("light:off", f.Events);
    }

    [Fact]
    public async Task A_failed_handover_returns_to_warm_fallback_and_releases_the_cover()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Device.Succeeds = false;
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.True(f.Light.Active);
        Assert.False(f.Cover.Visible);
        Assert.Equal(new[] { "light:on", "wait:250", "cover:hidden" }, f.Events.TakeLast(3));
        var writes = f.Device.Writes.Count;
        f.Gamma.RecoveryTick();
        f.Gamma.WatchdogTick();
        Assert.Equal(writes, f.Device.Writes.Count);
        f.Device.Succeeds = true;
        f.Gamma.Recover();
        await f.Protection.PendingTransition;
        Assert.False(f.Light.Active);
        Assert.Empty(f.Gamma.FailedDisplayIds);
    }

    [Fact]
    public async Task A_rejected_night_light_off_does_not_apply_a_second_filter()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Light.RejectOff = true;
        f.Device.Writes.Clear();
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.True(f.Light.Active);
        Assert.Empty(f.Device.Writes);
        Assert.False(f.Cover.Visible);
    }

    [Fact]
    public void Disabled_protection_leaves_night_light_and_cover_untouched()
    {
        using var f = new Fixture(enabled: false);
        f.Protection.Initialize();
        f.Events.Clear();
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        Assert.DoesNotContain(f.Events, e => e.StartsWith("cover:") || e.StartsWith("light:"));
    }

    [Fact]
    public async Task A_persisted_recovery_token_restores_the_original_off_state_after_a_crash()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "candlelight-guard-" + Guid.NewGuid() + ".state"
        );
        File.WriteAllText(path, "off");
        try
        {
            using (var f = new Fixture(path, initiallyOn: true))
            {
                f.Protection.Initialize();
                await f.Protection.PendingTransition;
                Assert.False(f.Light.Active);
            }
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Fail_safe_never_exits_during_sleep_but_bounds_a_stuck_awake_cover()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Gamma.OnDisplayState(0);
        f.Now = 100000;
        f.Protection.CheckFailSafe();
        Assert.Equal(0, f.EmergencyExits);
        f.Cover.Paint = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Gamma.OnDisplayState(1);
        f.Now += 8000;
        f.Protection.CheckFailSafe();
        Assert.True(f.Light.Active);
        Assert.Equal(1, f.EmergencyExits);
        var pending = f.Protection.PendingTransition;
        f.Protection.Dispose();
        await pending;
    }

    [Fact]
    public async Task Session_lock_keeps_the_windows_fallback_until_unlock()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Device.Writes.Clear();
        f.Protection.OnSessionLockChanged(true);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.True(f.Light.Active);
        Assert.True(f.Cover.Visible);
        Assert.Empty(f.Device.Writes);
        f.Protection.OnSessionLockChanged(false);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.False(f.Light.Active);
        Assert.False(f.Cover.Visible);
        Assert.Equal(2, f.Device.Writes.Count);
    }

    [Fact]
    public async Task Disabling_protection_after_a_completed_cycle_skips_the_next_cover()
    {
        using var f = new Fixture();
        f.Protection.Initialize();
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        f.Settings.IsNightLightProtectionEnabled = false;
        f.Events.Clear();
        f.Gamma.OnDisplayState(0);
        f.Gamma.OnDisplayState(1);
        await f.Protection.PendingTransition;
        Assert.DoesNotContain(f.Events, e => e.StartsWith("cover:") || e.StartsWith("light:"));
    }
}
