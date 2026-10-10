# Protected desktop prototype (0.2.7)

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
a fullscreen matrix or system-cursor leases. The ordinary engine suspends updates
and hides local surfaces while its input desktop is inactive, then reapplies its
profiles on return. Secure cursor filtering has not been implemented in this
prototype. It can idle on Winlogon and re-read transferred settings when
reactivated. However, on this PC the UAC instance disappears when the prompt
closes. The 0.2.7 diagnostic confirms that Windows launches it inside a job,
despite both registrations specifying `TerminateOnDesktopSwitch=0`. Persistence
between UAC prompts must therefore not be assumed.

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
launches the process. No service, token manipulation or UAC-policy change is used.

Publish the optimized assemblies with:

```powershell
dotnet publish Candlelight.Next/Candlelight.Next.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -p:UseAppHost=false -o artifacts/secure-desktop/ready-to-run -m:1
```

`scripts/install-secure-startup.ps1` verifies the pinned installed baseline and
prepared update manifest, then clones the protected runtime and replaces only
the two application assemblies and their symbols. It retains the existing signed
hosts and self-contained runtime, checks hashes/signatures/ordinary-user write
permissions, and updates the two AT registration paths. The older version is
retained for rollback.

The SYSTEM helper writes a bounded diagnostic snapshot while active and on
desktop transitions, without screen
images, under `%ProgramData%\Candlelight\SecureDesktop\session-<id>.json`.
The installer protects that directory against ordinary-user writes. This
snapshot reports API/source-update state, not actual scanout or first-frame
coverage.

Validation: 166 unit tests, including native session-owner identity resolution,
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
364 ms for profile/source preparation after the cover was ready. Physical startup
appearance on 0.2.7 still requires the user's observation.
No guarantee of a filtered first frame, filtered cursor, logon or suspend/wake
behavior is made until physical tests pass.

Source: [Microsoft AT registration and secure-desktop settings transfer](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration).
Startup optimization: [Microsoft ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run).
