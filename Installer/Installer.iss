#define AppName "Candlelight"
#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by build-installer.ps1
#endif
#ifndef DistDir
  #define DistDir "bin"
#endif

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
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableReadyPage=no
WizardStyle=modern
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\Candlelight.exe
OutputDir={#DistDir}
OutputBaseFilename=Candlelight-{#AppVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Candlelight
VersionInfoDescription=Candlelight installer
VersionInfoProductName=Candlelight

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
portuguese.DesktopShortcut=Criar atalho no ambiente de trabalho
portuguese.AutoStart=Iniciar com o Windows
english.DesktopShortcut=Create a desktop shortcut
english.AutoStart=Start with Windows
portuguese.ConfigureNightLight=Configurar a Luz noturna do Windows (recomendado)
english.ConfigureNightLight=Set up Windows Night Light (recommended)

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"
Name: "autostart"; Description: "{cm:AutoStart}"

[Files]
Source: ".installed"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "Settings.json,ColorStatus.txt,NightLightGuard.state*,.installed"; Flags: ignoreversion recursesubdirs
; Preserve settings when installing over an earlier portable copy.
Source: "{app}\Settings.json"; DestDir: "{userappdata}\Candlelight"; Flags: external onlyifdoesntexist skipifsourcedoesntexist uninsneveruninstall
Source: "{app}\NightLightGuard.state"; DestDir: "{userappdata}\Candlelight"; Flags: external onlyifdoesntexist skipifsourcedoesntexist uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Candlelight.exe"; IconFilename: "{app}\Candlelight.exe"
Name: "{autodesktop}\Candlelight"; Filename: "{app}\Candlelight.exe"; IconFilename: "{app}\Candlelight.exe"; Tasks: desktopicon

[InstallDelete]
; Replaced by the installer-based updater in 0.6.3.
Type: files; Name: "{app}\Onova.dll"
Type: files; Name: "{app}\Onova.pdb"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Candlelight"; ValueData: """{app}\Candlelight.exe"" --start-hidden"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Candlelight.exe"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "ms-settings:nightlight"; Description: "{cm:ConfigureNightLight}"; Flags: shellexec nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\ColorStatus.txt"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingRun: String;
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Candlelight', ExistingRun) then
      if ExistingRun = '"' + ExpandConstant('{app}\Candlelight.exe') + '" --start-hidden' then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Candlelight');
  end;
end;
