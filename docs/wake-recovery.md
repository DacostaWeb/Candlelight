# Wake recovery and remaining hardware verification

Candlelight applies current per-monitor targets on `PBT_APMRESUMEAUTOMATIC`,
`PBT_APMRESUMESUSPEND`, session unlock/connect, display power-on and display
topology changes. Its hidden notification window is a top-level window because
message-only windows do not receive system broadcasts. Application startup and
resume skip the daylight-to-night smoothing path. A serialized worker retries
every 50 ms for five seconds, skipping ticks when the driver is busy. Display-off
notifications stop writes and retries. Context recreation does not reset gamma.

Automated tests use fake devices and a controlled clock to verify monitor routing,
immediate night values on wake, retry expiry, display-off, driver failure and
polling. The compiled UI preview checks real preset/row command bindings,
independent profiles, schedule editing and persistence without touching gamma.

The connected HDMI monitor is detected as `13A1F`; both active displays have a
physical EDID identity. No real sleep, hibernation, lid-close, gamma readback or
USB-C reconnection test has been performed. USB-C profile reuse depends on the
monitor reporting the same manufacturer, product and serial across inputs.
Monitors without usable serials fall back to their connection path. Simultaneous
duplicate serials are disambiguated by connection.

To verify on hardware, close other gamma applications, select the OLED, apply
the 500 K preset and leave the internal display at 100% brightness. Compare
display-off/on, sleep/resume, hibernation/resume and lid-close/open in separate
runs. Record whether there is a normal-color frame before Windows dispatches the
resume notification. That interval cannot be eliminated by a user-space gamma
application alone. Check gamma on each display again after reconnecting.

## Night Light as an additional guard

The proposed sequence is to arm Night Light before suspension or lid-close,
reapply Candlelight on wake, then restore the previous Night Light state and
reapply Candlelight again. It remains an experimental candidate, not part of
v0.1.0. It must first be established that Night Light covers the earliest visible
frame on this hardware and does not introduce an extra gamma reset on handover.
Automation would need version-aware CloudStore decoding, preservation of unknown
fields, and restoration of the user's original state instead of forcing it off.
The existing Night Light strength and schedule should remain untouched.

Sources:

- [Microsoft: SetDeviceGammaRamp limitations](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp)
- [Microsoft: message-only windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#message-only-windows)
- [Microsoft: automatic resume notification](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmresumeautomatic)
- [Microsoft: monitor identifying information](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorid)
- [Night Light control implementation and reverse-engineered schema](https://github.com/kvnxiao/win-nightlight-cli)
