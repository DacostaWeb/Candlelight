using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LightBulb.PlatformInterop;
using LightBulb.PlatformInterop.Internal;
using Xunit;

namespace LightBulb.Core.Tests;

public class PowerSettingNotificationSpecs
{
    private static void Dispatch(Guid incoming, uint length, int value, Action<int> callback)
    {
        var buffer = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(new PowerBroadcastSetting(incoming, length), buffer, false);
            Marshal.WriteInt32(buffer, 20, value);
            PowerSettingNotification.DispatchDisplayState(
                PowerSettingNotification.Ids.ConsoleDisplayStateChanged,
                new WndProcMessage(0x218, 0x8013, buffer),
                callback
            );
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void Power_saving_zero_cannot_signal_that_the_display_is_off()
    {
        var states = new List<int>();
        Dispatch(PowerSettingNotification.Ids.PowerSavingStatusChanged, 4, 0, states.Add);
        Dispatch(PowerSettingNotification.Ids.ConsoleDisplayStateChanged, 4, 1, states.Add);
        Assert.Equal([1], states);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Actual_display_off_on_and_dimmed_states_are_forwarded(int value)
    {
        var states = new List<int>();
        Dispatch(PowerSettingNotification.Ids.ConsoleDisplayStateChanged, 4, value, states.Add);
        Assert.Equal([value], states);
    }

    [Fact]
    public void Truncated_display_state_is_ignored()
    {
        var states = new List<int>();
        Dispatch(PowerSettingNotification.Ids.ConsoleDisplayStateChanged, 1, 0, states.Add);
        Assert.Empty(states);
    }
}
