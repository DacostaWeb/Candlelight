param([string]$Runtime = 'win-x64', [string]$DotNetExecutable = 'dotnet')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot "artifacts/Candlelight.$Runtime"
$zipPath = Join-Path $projectRoot "artifacts/Candlelight.$Runtime.zip"

& $DotNetExecutable publish (Join-Path $projectRoot 'LightBulb/LightBulb.csproj') --configuration Release --runtime $Runtime --self-contained --output $outputDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing Candlelight failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'License.txt'), (Join-Path $projectRoot 'THIRD_PARTY.md'), (Join-Path $projectRoot 'ReadMe.md') -Destination $outputDir
$docsDir = Join-Path $outputDir 'docs'
New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs/*') -Destination $docsDir -Recurse -Force
Compress-Archive -Path (Join-Path $outputDir '*') -DestinationPath $zipPath -Force
Write-Output $zipPath
