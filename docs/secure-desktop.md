# Protected desktop prototype (0.2.6)

Windows can launch registered assistive technology on the UAC/logon desktop.
The ordinary UIAccess process is insufficient. This prototype registers
`Candlelight_ColorFilter_v1`, with the alternate
`Candlelight_ColorFilterSecure_v1` for Winlogon. It leaves UAC and secure-desktop
policies enabled.

The alternate image is an unchanged copy of the already signed apphost, named
`Candlelight.Secure.exe`, loading the updated, protected application assemblies.
It accepts only `--secure-desktop`, requires SYSTEM and the Winlogon desktop,
and never creates the ordinary control window, tray, settings store or pipe.
The installer reuses the existing trusted certificate; no additional trust is
installed. All runtime files are installed in Program Files with ordinary-user
read access only.

The ordinary controller publishes monitor IDs, enable flags, manual colors and
active schedules under the documented ATConfig registry location. Windows copies
this configuration to the secure desktop. The reader bounds registry allocation
before reading, limits JSON depth/size, rejects unknown fields and invalid color
ranges, and checks monitor/schedule counts, duplicate IDs and times. Transferred
data contains no commands, plugins, file paths or executable names. Unknown
connected monitors retain their ordinary colors.

The secure renderer uses independent 1x windowed magnification surfaces, without
a fullscreen matrix or system-cursor leases. The ordinary engine suspends updates
and hides local surfaces while its input desktop is inactive, then reapplies its
profiles on return. Secure cursor filtering has not been implemented in this
prototype. The separate instance remains idle on Winlogon between transitions;
it re-reads transferred settings when reactivated.

The SYSTEM helper writes a bounded diagnostic snapshot while active and on
desktop transitions, without screen
images, under `%ProgramData%\Candlelight\SecureDesktop\session-<id>.json`.
The installer protects that directory against ordinary-user writes. This
snapshot reports API/source-update state, not actual scanout or first-frame
coverage.

Validation: 165 unit tests, bounded settings transfer rejection tests, native
resource resolution, rejection of ordinary-desktop helper launches, and the
existing mixed-monitor/cursor probe plus a pixel check of the isolated renderer
configuration on the ordinary desktop. A real UAC test must separately confirm
that Windows launches the alternate image, transfers profiles and displays
correct colors on both monitors. Registration alone is not proof of these.
No guarantee of a filtered first frame, filtered cursor, logon or suspend/wake
behavior is made until physical tests pass.

Source: [Microsoft AT registration and secure-desktop settings transfer](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration).
