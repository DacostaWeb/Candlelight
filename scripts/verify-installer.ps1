param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{D3B2792A-54C7-4A18-92EC-E9E6530BAC11}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'Run this verification before installing Candlelight, or on a clean test machine.' }
if (Get-Process -Name Candlelight -ErrorAction SilentlyContinue) { throw 'Close Candlelight before verifying the installer.' }
$testRoot = Join-Path $env:TEMP ('Candlelight-Installer-Verification-' + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $testRoot 'Application'
$previewDir = Join-Path $testRoot 'Preview'
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Invoke-Setup([string]$LogName) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS="" /GROUP="Candlelight Installer Verification" /DIR="' + $appDir + '" /LOG="' + (Join-Path $testRoot $LogName) + '"'
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw 'Installer verification timed out.' }
    if ($process.ExitCode -ne 0) { throw ('Installer failed: ' + $process.ExitCode + '; see ' + $testRoot) }
}
$runPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
$originalRun = Get-ItemPropertyValue -LiteralPath $runPath -Name Candlelight -ErrorAction SilentlyContinue
$settingsPath = Join-Path $env:APPDATA 'Candlelight/Settings.json'
$originalSettingsHash = if (Test-Path $settingsPath) { (Get-FileHash $settingsPath).Hash } else { $null }
Invoke-Setup 'install.log'
foreach ($name in @('Candlelight.exe','.installed','unins000.exe','coreclr.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $appDir $name))) { throw ('Missing installed file: ' + $name) }
}
if (!(Test-Path -LiteralPath $uninstallKey)) { throw 'Uninstaller not registered.' }
$preview = Start-Process -FilePath (Join-Path $appDir 'Candlelight.exe') -ArgumentList ('--preview "--capture-preview=' + $previewDir + '"') -WindowStyle Hidden -PassThru
if (!$preview.WaitForExit(60000) -or $preview.ExitCode -ne 0) { throw 'Installed application preview failed.' }
if (!(Get-Content -LiteralPath (Join-Path $previewDir 'verification.txt') -Raw).StartsWith('PASS:')) { throw 'UI verification did not pass.' }
$exeHash = (Get-FileHash (Join-Path $appDir 'Candlelight.exe')).Hash
Invoke-Setup 'upgrade.log'
if ((Get-FileHash (Join-Path $appDir 'Candlelight.exe')).Hash -ne $exeHash) { throw 'Upgrade changed the executable unexpectedly.' }
$uninstall = Start-Process -FilePath (Join-Path $appDir 'unins000.exe') -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $testRoot 'uninstall.log') + '"') -WindowStyle Hidden -PassThru
if (!$uninstall.WaitForExit(60000) -or $uninstall.ExitCode -ne 0) { throw 'Uninstaller failed.' }
if ((Test-Path (Join-Path $appDir 'Candlelight.exe')) -or (Test-Path $uninstallKey)) { throw 'Uninstall left the program or registration behind.' }
$finalRun = Get-ItemPropertyValue -LiteralPath $runPath -Name Candlelight -ErrorAction SilentlyContinue
if ($originalRun -ne $finalRun) { throw 'An unselected startup task changed the existing startup entry.' }
$finalSettingsHash = if (Test-Path $settingsPath) { (Get-FileHash $settingsPath).Hash } else { $null }
if ($originalSettingsHash -ne $finalSettingsHash) { throw 'Installation modified existing user settings.' }
Write-Output 'PASS: clean install, self-contained runtime, installed UI, repeat install, uninstall, startup isolation and settings preservation.'
Write-Output ('Verification files: ' + $testRoot)
