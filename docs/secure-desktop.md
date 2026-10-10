# Protected desktop prototype (0.2.9)

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
this configuration to the secure desktop where supported. In the actual UAC test
on this PC, Windows launched the alternate renderer as SYSTEM on Winlogon but
did not provide the copied configuration. The installed transfer hotfix uses
`WTSQuerySessionInformation` to identify the signed-in owner of its own session,
resolves that account's SID, and reads only Candlelight's fixed ATConfig location
in that user's loaded registry hive. It does not acquire a user token, impersonate,
change registry permissions or accept a source path from settings.
The reader bounds registry allocation
before reading, limits JSON depth/size, rejects unknown fields and invalid color
ranges, and checks monitor/schedule counts, duplicate IDs and times. Transferred
data contains no commands, plugins, file paths or executable names. Unknown
connected monitors retain their ordinary colors.

The secure renderer uses independent 1x windowed magnification surfaces, without
a fullscreen matrix or system-cursor leases. Secure cursor filtering has not
been implemented in this prototype. The renderer can idle on Winlogon and
re-read transferred settings when reactivated. In 0.2.7, Windows launched the
UAC instance inside a job and terminated it when the prompt closed, despite
both registrations specifying `TerminateOnDesktopSwitch=0`.

Version 0.2.8 adds the automatic LocalSystem service
`Candlelight.SecureBroker`. It prepares one renderer on the inactive Winlogon
desktop of the signed-in active console session, before a UAC request occurs.
The service accepts SCM stop/interrogate controls only and has no command pipe,
network listener or UI automation. It reads only the fixed Accessibility
registration enable flag in the session owner's loaded hive. It does not read
commands or executable paths from user settings.

The service duplicates its own SYSTEM primary token, selects the console
session and enables UIAccess for the existing signed renderer. It never acquires
or impersonates a user's token. SCM restricts its required privileges to
`SeTcbPrivilege`, `SeAssignPrimaryTokenPrivilege` and `SeIncreaseQuotaPrivilege`
(Windows retains `SeChangeNotifyPrivilege`). The executable, argument and desktop
are fixed: the protected sibling `Candlelight.Secure.exe --secure-desktop` on
`WinSta0\\Winlogon`. A suspended child is assigned to an owned kill-on-close job
before execution. Stopping the service, disabling the registration, logging off
or changing console owner removes only this owned child. No Windows startup job
is escaped. The ordinary service host has `uiAccess=false`; the existing signed
normal and secure hosts retain `uiAccess=true`.

While Winlogon is inactive, prepared local surfaces remain black and visible on
that invisible desktop. The isolated engine cannot prepare inactive surfaces
when fullscreen effects or system-cursor management are enabled. It makes no
shared matrix or cursor writes while inactive. On reactivation it refreshes its
profiles and sources.

Version 0.2.9 also retains the ordinary engine's existing local surfaces at black
on its inactive Default desktop, instead of hiding them. On return, it applies a
300 ms black recovery interval before restoring the shared and local colors.
The ordinary engine skips source, fullscreen-effect and cursor updates while its
desktop is inactive. This covers the local OLED surface while the normal
renderer resumes; it does not claim that a timer or matrix readback measures
physical first-frame light.

The user confirmed that 0.2.6 eventually filters both the UAC window and its
background with the correct monitor profiles, but exposes unfiltered light for
about one or two seconds at startup. Version 0.2.7 separates the small secure
entry point from the ordinary WinForms entry point and publishes the updated
assemblies as ReadyToRun to reduce startup compilation work. It creates a native
black cover before reading profiles or initializing magnification. The cover has
its own message loop, never activates, has no controls and does not inject input.
Its independent two-second timeout releases it even if renderer initialization
stalls. Renderer sources exclude the cover to avoid capturing black recursively.
The cover is removed after source preparation and a compositor flush; this is
not a physical first-frame guarantee. It cannot cover the interval before Windows
launches the process. The user still saw initial white light on 0.2.7; this led
to the prewarming service in 0.2.8. UAC policy remains unchanged.

Publish the optimized assemblies with:

```powershell
dotnet publish Candlelight.Next/Candlelight.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -p:UseAppHost=true -p:CandlelightUIAccess=false -o artifacts/secure-desktop/return-handoff -m:1
```

The ordinary published apphost is copied to `Candlelight.SecureBroker.exe` in
the prepared package. It must not replace the installed signed normal/secure
hosts. `scripts/install-secure-prewarm.ps1` upgrades the pinned 0.2.7 installation
to 0.2.8 and creates the service. `scripts/install-secure-handoff.ps1` upgrades
the pinned successful 0.2.8 installation to 0.2.9 and updates the existing service
executable path. These prototype upgrade scripts verify prepared manifests and
installed hashes, clone the protected runtime, replace the two application
assemblies/symbols and add the ordinary broker host. They retain the signed
normal/secure hosts and self-contained runtime, verify signatures and protected
permissions, update AT registration paths, and require a matching live inactive
renderer with prepared surfaces. Failure restores registrations and the prior
service path, or removes the newly created service. Older runtimes are retained
for rollback; no new certificate trust is installed.

The SYSTEM helper writes a bounded diagnostic snapshot while active and on
desktop transitions, without screen
images, under `%ProgramData%\Candlelight\SecureDesktop\session-<id>.json`.
The service writes ownership/preparation state to `broker.json` in the same
protected directory. These snapshots report API/source-update state, not actual
scanout or first-frame coverage. An inactive helper may report its last active
engine snapshot; use its top-level `active` flag when interpreting that snapshot.

Validation: 168 unit tests, including isolation guards, native session-owner identity resolution,
bounded settings transfer rejection tests, native
resource resolution, rejection of ordinary-desktop helper launches, and the
existing mixed-monitor/cursor probe plus a pixel check of the isolated renderer
configuration on the ordinary desktop. Real UAC tests confirmed a SYSTEM
Winlogon instance with both monitor profiles, two local surfaces, advancing
source-update counts and no reported render errors. On return the ordinary
renderer resumes with its original profiles and native cursor filter. This is
API/runtime evidence. The user confirmed eventual physical appearance on 0.2.6,
including the initial delay described above. For 0.2.7, a native black-pixel probe
on both monitors verified the startup cover and its timeout. The mixed renderer
probe verified owned color patches while excluding that cover and again after
its removal. A real UAC test reported both profiles, no cover/render errors and
364 ms for profile/source preparation after the cover was ready, but the user
still saw white light at entry. On 0.2.8, the user saw no entry flash but reported
a possible OLED flash after accepting the prompt.

On 0.2.9, the mixed-monitor native probe passed exact local red/brightness
checks, matrix composition, pause isolation, black/resume, cursor restoration
and the isolated secure configuration. A lifecycle check confirmed that exiting
the ordinary app removes the prewarmed helper and restarting prepares it again.
Repeated real UAC requests kept the same prepared SYSTEM Winlogon PID alive;
the ordinary engine returned to internal 2700 K / 100% and OLED pure red / 15%,
with no reported errors. On the repeated physical test on this HDMI setup, the
user reported no flash after accepting; the entry flash had already disappeared
in the preceding test. This is a successful observation on this setup, not a
guarantee for every desktop transition. Cold logon, RDP, suspend/wake and protected
cursor filtering remain unverified by this change.

Source: [Microsoft AT registration and secure-desktop settings transfer](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration).
Startup optimization: [Microsoft ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run).
Service launch: [Microsoft CreateProcessAsUser](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw),
[SetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-settokeninformation),
[required service privileges](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_required_privileges_infow),
and [job lifetime limits](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information).
