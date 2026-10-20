param(
    [string]$PackageDirectory,
    [string]$OutputDirectory,
    [string]$IsccExecutable
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$PackageDirectory) { $PackageDirectory = Join-Path $projectRoot 'artifacts/Candlelight.win-x64' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/release' }
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (!$IsccExecutable) {
    $candidates = @(
        (Join-Path $projectRoot '.tools/inno/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
    )
    $IsccExecutable = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$IsccExecutable) { throw 'Install Inno Setup 6.3+ or pass -IsccExecutable.' }
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$version = [string]$buildProperties.Project.PropertyGroup.Version
$executable = Join-Path $package 'Candlelight.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Run scripts/publish.ps1 first.' }
$binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
if ([version]$binaryVersion -ne [version]($version + '.0')) { throw 'Package version does not match Directory.Build.props.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $IsccExecutable '/Qp' "/DAppVersion=$version" "/DSourceDir=$package" "/DDistDir=$output" (Join-Path $projectRoot 'Installer/Installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
$installer = Join-Path $output "Candlelight-$version-Setup-win-x64.exe"
if (!(Test-Path -LiteralPath $installer)) { throw 'The installer was not produced.' }
Write-Output $installer
