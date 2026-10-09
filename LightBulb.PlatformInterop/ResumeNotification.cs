using System;
using LightBulb.PlatformInterop.Internal;
using PowerKit;

namespace LightBulb.PlatformInterop;

public static class ResumeNotification
{
    public static IDisposable Register(
        Action callback,
        Action? suspend = null,
        Action<bool>? sessionLockChanged = null
    )
    {
        var window = WndProcSponge.Default;
        var power = window.Listen(
            0x218,
            message =>
            {
                // PBT_APMRESUMEAUTOMATIC, PBT_APMRESUMESUSPEND, PBT_APMRESUMECRITICAL.
                if (message.WParam is 0x12 or 0x07 or 0x06)
                    callback();
                else if (message.WParam == 0x04)
                    suspend?.Invoke();
            }
        );
        var session = window.Listen(
            0x2B1,
            message =>
            {
                if (message.WParam == 0x07)
                    sessionLockChanged?.Invoke(true);
                else if (message.WParam == 0x08)
                    sessionLockChanged?.Invoke(false);
                // WTS_SESSION_UNLOCK, WTS_CONSOLE_CONNECT, WTS_REMOTE_CONNECT.
                if (message.WParam is 0x08 or 0x01 or 0x03)
                    callback();
            }
        );
        var registered = NativeMethods.WTSRegisterSessionNotification(window.Handle, 0);
        return Disposable.Merge(
            power,
            session,
            Disposable.Create(() =>
            {
                if (registered)
                    NativeMethods.WTSUnRegisterSessionNotification(window.Handle);
            })
        );
    }
}
