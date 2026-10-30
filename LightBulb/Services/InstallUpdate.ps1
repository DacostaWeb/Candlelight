param(
    [Parameter(Mandatory)][int]$ParentProcessId,
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$ExpectedSha256,
    [ValidateSet('yes', 'no')][string]$AutoStart = 'no'
)
$ErrorActionPreference = 'Stop'
$installerPath = Join-Path $PSScriptRoot 'Candlelight-Setup-win-x64.exe'
$readyPath = Join-Path $PSScriptRoot 'ready'
$resultPath = Join-Path $PSScriptRoot 'last-result.txt'
$parentExited = $false
try {
    if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash -ine $ExpectedSha256) {
        throw 'Installer checksum mismatch.'
    }
    $parentProcess = Get-Process -Id $ParentProcessId -ErrorAction Stop
    $null = $parentProcess.Handle
    Set-Content -LiteralPath $readyPath -Value 'ready'
    if (!$parentProcess.WaitForExit(30000)) { throw 'Candlelight did not exit; update cancelled.' }
    $parentExited = $true
    $applicationDirectory = Split-Path -Parent $ApplicationPath
    $startupTask = if ($AutoStart -eq 'yes') { 'autostart' } else { '!autostart' }
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NORESTARTAPPLICATIONS /MERGETASKS="' + $startupTask + '" /DIR="' + $applicationDirectory + '"'
    $installer = Start-Process -FilePath $installerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $installer.WaitForExit()
    if ($installer.ExitCode -ne 0) { throw ('Installer failed: ' + $installer.ExitCode) }
    Set-Content -LiteralPath $resultPath -Value 'success'
} catch {
    Set-Content -LiteralPath $resultPath -Value ('failed: ' + $_.Exception.Message)
} finally {
    # Remove only this updater's known files, never recursively delete a directory.
    Remove-Item -LiteralPath $readyPath, $installerPath, $PSCommandPath -Force -ErrorAction SilentlyContinue
    if ($parentExited -and (Test-Path -LiteralPath $ApplicationPath)) {
        Start-Process -FilePath $ApplicationPath -WindowStyle Hidden | Out-Null
    }
}
