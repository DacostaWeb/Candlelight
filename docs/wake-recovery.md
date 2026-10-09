# Wake recovery and remaining hardware verification

Candlelight applies current per-monitor targets on `PBT_APMRESUMEAUTOMATIC`,
`PBT_APMRESUMESUSPEND`, session unlock/connect, display power-on and display
topology changes. Its hidden notification window is a top-level window because
message-only windows do not receive system broadcasts. Application startup and
resume skip the daylight-to-night smoothing path. A serialized worker retries
every 50 ms for five seconds, skipping ticks when the driver is busy. Display-off
notifications stop writes and retries. Context recreation does not reset gamma.
Display-state listeners filter the notification GUID before reading its value:
an unrelated power-saving value of zero must not block writes as if the display
were off. Regression tests dispatch real-format native notification buffers.

Automated tests use fake devices and a controlled clock to verify monitor routing,
immediate night values on wake, retry expiry, display-off, driver failure and
polling. The compiled UI preview checks real preset/row command bindings,
independent profiles, schedule editing and persistence without touching gamma.

The HDMI monitor was detected as `13A1F`; both it and the internal display have a
physical EDID identity. A live Windows color-system write of 500 K / 15% on the
OLED was accepted and read back as R=9754, G=0, B=0; the user confirmed it became
red and dim. HDR/advanced color was disabled on both displays. Public GDI writes
accepted moderate warmth but rejected this red/dim target despite the registry
unlock already being present. The upstream project documents that the unlock may
require a restart or a new Windows session.

Version 0.1.1 adds a verified per-monitor fallback using dynamically resolved
`mscms.dll` exports `InternalSetDeviceGammaRamp` and `InternalGetAppliedGammaRamp`.
These are undocumented Windows APIs and may change across OS versions. Both must
exist to use this path. Failures and readback mismatches remain visible instead
of being reported as success. After installing the color-system target, GDI is
reset to an exact identity LUT to prevent the two filters multiplying. The
backend choice survives context recreation and both layers are reset on exit.
Existing nonidentity color-system LUTs are detected even in a new process, so a
successful GDI write cannot leave an earlier filter stacked above the controls.
Recovery callbacks are gated until dashboard initialization, preventing a default
daytime write before settings have loaded. The compiled UI verification additionally
exercises numeric inputs and sliders rather than only assigning model properties.
Automated tests cover rejected/silently ignored writes, unavailable exports,
daytime transitions, context recreation, reset order and exact-zero validation.
This compatibility implementation is original C# code; the export ABI was
cross-checked with the [KelvinShift gamma implementation](https://github.com/mackid1993/KelvinShift/blob/main/win32/src/GammaService.cpp).

The user confirmed that the application's numeric controls, sliders and presets
now change the internal display's color. At 2700 K / 100%, closing and reopening
the lid preserved the warm color. A separate suspension test used Modern Standby
(confirmed by Kernel-Power events 506/507): the first visible image was warm,
then a brief blue/neutral flash occurred and the warm color returned. The earlier
status log did not record individual recovery writes, so it cannot establish
whether Windows or Candlelight caused that intermediate frame.

Version 0.1.2 removes redundant operations in this path. Target recalculation
queues the request and recovery writes it once, instead of twice immediately.
The color-system path reads GDI first and leaves an already neutral LUT untouched;
if neutralization is needed, it occurs after installing the desired filter and is
verified. In this version the color filter is refreshed during the five-second
recovery window because driver state can change independently of cached LUT readback.
The bounded `ColorStatus.txt` now records power-event sources and each recovery
write's before/after RGB values, whether GDI needed neutralizing, and call duration.
These changes require a new physical suspension test before claiming the flash
is resolved.

The repeat test of 0.1.2 still produced the flash, on the unlocked desktop.
Its recovery trace recorded 92 writes, all with the warm target already present
before the write, GDI identity retained, and successful warm readback afterwards.
The first write took 379.9 ms. This does not prove which component caused the
visible frame: LUT readback may not expose every transient in the display pipeline.
The next diagnostic build verifies both LUT layers before an automatic update
and only writes on mismatch or unreadable state. Explicit **Reaplicar** still
forces a write. The repeat physical test still produced the flash on the desktop.
There were 99 recovery checks: the first found an already neutral color-system
LUT (65535/65535/65535) and restored the warm target in 265.1 ms; the remaining
98 checks found the correct target and performed no writes. This supports an
external color reset preceding Candlelight's correction, although it does not
identify the responsible Windows/driver component. Avoiding unnecessary writes
remains useful, but is not a demonstrated fix for this resume flash.

No real hibernation or USB-C reconnection test has been performed. The OLED was
subsequently disconnected. The production C# `DeviceContext` was
then exercised on the internal panel at 2700 K / 100% brightness: its color-system
readback was R=65026, G=42514, B=22289 and GDI was an exact identity (65535 in each
channel). The user confirmed the visible color change on the internal screen.
USB-C profile reuse depends on the
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
the current release. It must first be established that Night Light covers the earliest visible
frame on this hardware and does not introduce an extra gamma reset on handover.
Automation would need version-aware CloudStore decoding, preservation of unknown
fields, and restoration of the user's original state instead of forcing it off.
The existing Night Light strength and schedule should remain untouched.

On this Windows 11 build 26200 device, a diagnostic-only strict Bond v1 codec
successfully round-tripped the existing state and rejected unknown schemas and
truncated data before any registry write. Original state/settings blobs were
backed up locally. Only the state was changed from off to on; the strength and
disabled schedule were left untouched. With Candlelight stopped, readback changed
to R=65535, G=18739, B=0, showing that Night Light uses the same color-system LUT
on this device. A separate physical suspension test of Night Light is pending.
The diagnostic switch is not installed as an automatic application feature.

Sources:

- [Microsoft: SetDeviceGammaRamp limitations](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp)
- [Microsoft: message-only windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#message-only-windows)
- [Microsoft: automatic resume notification](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmresumeautomatic)
- [Microsoft: Modern Standby resume order](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby#resume-from-modern-standby)
- [Microsoft: monitor identifying information](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorid)
- [Night Light control implementation and reverse-engineered schema](https://github.com/kvnxiao/win-nightlight-cli)
- [Upstream gamma-range restart report](https://github.com/Tyrrrz/LightBulb/issues/253)
