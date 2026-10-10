#define AppName "Candlelight"
#define AppVersion GetEnv("INSTALLER_APP_VERSION")

[Setup]
AppId={{D3B2792A-54C7-4A18-92EC-E9E6530BAC11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher="Candlelight contributors"
AppPublisherURL="https://github.com/DacostaWeb/Candlelight"
AppSupportURL="https://github.com/DacostaWeb/Candlelight/issues"
AppUpdatesURL="https://github.com/DacostaWeb/Candlelight/releases"
AppMutex=Candlelight_Identity
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableWelcomePage=yes
DisableProgramGroupPage=no
DisableReadyPage=yes
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\Candlelight.exe
OutputDir=bin\
OutputBaseFilename=Candlelight-Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: ".installed"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\License.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "Source\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Candlelight.exe"; IconFilename: "{app}\Candlelight.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Candlelight.exe"
Name: "{group}\{#AppName} on Github"; Filename: "https://github.com/DacostaWeb/Candlelight"; IconFilename: "{app}\Candlelight.exe"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\ICM"; ValueType: dword; ValueName: "GdiICMGammaRange"; ValueData: "256"

[Run]
Filename: "{app}\Candlelight.exe"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Name: "{userappdata}\Candlelight"; Type: filesandordirs
