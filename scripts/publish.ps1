param(
    [string]$Runtime = 'win-x64',
    [string]$DotNetExecutable = 'dotnet',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts/Candlelight.$Runtime" }
$zipPath = "$outputDir.zip"

& $DotNetExecutable publish (Join-Path $projectRoot 'LightBulb/LightBulb.csproj') --configuration Release --runtime $Runtime --self-contained --output $outputDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing Candlelight failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'License.txt'), (Join-Path $projectRoot 'THIRD_PARTY.md'), (Join-Path $projectRoot 'ReadMe.md') -Destination $outputDir
$docsDir = Join-Path $outputDir 'docs'
New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs/*') -Destination $docsDir -Recurse -Force
$packageFiles = Get-ChildItem -LiteralPath $outputDir | Where-Object { $_.Name -notin @('Settings.json', 'ColorStatus.txt', 'NightLightGuard.state', 'NightLightGuard.state.tmp', '.installed') }
Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath $zipPath -Force
Write-Output $zipPath
