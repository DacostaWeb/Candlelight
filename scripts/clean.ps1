param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# Git only removes ignored generated files in these explicit project directories.
# release/, source files, the installed application and settings are excluded.
$targets = @('artifacts/', 'Installer/bin/', 'Installer/Source/', 'LightBulb/bin/', 'LightBulb/obj/', 'LightBulb.Core/bin/', 'LightBulb.Core/obj/', 'LightBulb.Core.Tests/bin/', 'LightBulb.Core.Tests/obj/', 'LightBulb.PlatformInterop/bin/', 'LightBulb.PlatformInterop/obj/')
Push-Location $projectRoot
try {
    if ($Preview) { & git clean -ndX -- @targets }
    else { & git clean -fdX -- @targets }
    if ($LASTEXITCODE -ne 0) { throw 'Generated-file cleanup failed.' }
} finally { Pop-Location }
