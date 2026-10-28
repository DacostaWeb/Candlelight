# Candlelight

<p><img src="favicon.png" width="96" height="96" alt="Candlelight candle icon" /></p>

A Windows fork of [LightBulb](https://github.com/Tyrrrz/LightBulb) with independent
color temperature, brightness and schedules for each monitor.

**Version 0.6.0** adds daily solar transitions with editable curves to the
LightBulb-based engine and independent monitor controls. Each monitor has a
**Cores normais** button that preserves its program for later resumption.

![Candlelight interface](docs/interface.png)

## Install

Download **Candlelight-0.6.0-Setup-win-x64.exe** from
[the latest release](https://github.com/DacostaWeb/Candlelight/releases/latest).
Run it, choose whether to create a desktop shortcut and start with Windows, then
open Candlelight. The installer supports Windows 10 (build 19041+) and Windows 11
on x64-compatible PCs. It includes the .NET runtime and works offline.

Installation is per-user in `%LOCALAPPDATA%\Programs\Candlelight`. Settings are
stored in `%APPDATA%\Candlelight\Settings.json`. Installing over a portable copy
imports its settings if no installed settings already exist. Reinstalling keeps
the settings. Uninstall through **Windows Settings > Apps > Candlelight**;
uninstall removes the application and its shortcuts, and retains your settings.

The installer is not code-signed. Windows may display an unknown-publisher or
SmartScreen warning. The release includes SHA256 checksums; no certificates or
background services need to be installed. The application may separately offer
to unlock Windows' gamma range, which requires administrator approval.

A portable ZIP is also available: extract it and run `Candlelight.exe`.
Portable settings stay beside the executable. Close other gamma tools first.

## Controls

- Select a monitor in the left rail. Temperature and brightness apply to that
  monitor alone, including 500 K red-only output and software brightness down to 1%.
- Choose **Dia / noite**, **Manual**, or **Horários** independently per screen.
- In **Dia / noite**, set your city or coordinates once under **Localização do
  sol**. Sunrise and sunset update every day, including daylight saving
  changes, using the Windows time zone. New profiles default to 500 K at night
  and 2700 K during the day, with two-hour transitions.
- The morning transition **starts at sunrise**; the evening transition **ends at
  sunset**. Drag the two handles on each curve, choose a curve preset or edit its
  duration. Set night brightness to 25% for an OLED and 100% for an internal display
  to keep separate brightness levels while both reach 2700 K / 100% by day.
- **Cores normais** temporarily bypasses only the selected monitor's adjustments.
  **Retomar ajustes** returns to its saved program at the current time. This
  choice persists across restarts until switched off.
- **OLED vermelho** applies 500 K and 15% brightness to the selected monitor.
- Save your own presets, or use the built-in day, reading, night and red presets.
- Daily schedules support `HH:mm`, temperature, brightness and transition duration,
  including transitions across midnight. Changes save automatically.
- Physical monitor identity can retain profiles between HDMI and USB-C if the
  monitor reports the same serial on both connections.

This version targets SDR monitors on an extended desktop. Wake recovery and the
optional **Pausa a preto** guard are retained, but eliminating every white/blue
flash on resume is not guaranteed. See [the hardware findings](docs/wake-recovery.md).

## Build and verify

Requires .NET SDK 10 on Windows. Installer builds also require Inno Setup 6.3+.

```powershell
dotnet build LightBulb.slnx --configuration Release
dotnet test LightBulb.Core.Tests --configuration Release --no-build
dotnet LightBulb/bin/Release/net10.0-windows/Candlelight.dll --preview --capture-preview=artifacts/ui
./scripts/publish.ps1
./scripts/build-installer.ps1
./scripts/verify-installer.ps1 -InstallerPath artifacts/release/Candlelight-0.6.0-Setup-win-x64.exe
```

Installer verification runs on a machine without an installed/running Candlelight.
It checks installation, the included runtime, real compiled UI controls, repeat
installation, uninstall, settings preservation and startup isolation. Preview
mode uses synthetic displays and does not change screen colors or autostart.
CI publishes both an installer and a portable ZIP, without personal settings.

`--list-displays` and `--diagnose-displays=report.txt` inspect monitors read-only.
`CANDLELIGHT_SETTINGS_PATH` can override the settings location. Automatic upstream
updates are disabled for this fork.

## Credits

Based on LightBulb by Oleksii Holub, with exact-zero red-channel handling adapted
from [LightBulb Ahead](https://github.com/karipesonen/LightBulb-Ahead).
See [third-party credits](THIRD_PARTY.md) and the preserved [MIT license](License.txt).
