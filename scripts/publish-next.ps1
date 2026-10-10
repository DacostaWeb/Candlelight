param(
    [string]$DotNetExecutable = 'dotnet',
    [string]$OutputDirectory,
    [switch]$UIAccess
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts/Candlelight.Next.win-x64' }

& $DotNetExecutable publish (Join-Path $projectRoot 'Candlelight.Next/Candlelight.Next.csproj') --configuration Release --runtime win-x64 --self-contained --output $outputDir -m:1 "-p:CandlelightUIAccess=$($UIAccess.IsPresent.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw 'Publishing Candlelight Next failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'License.txt'), (Join-Path $projectRoot 'THIRD_PARTY.md') -Destination $outputDir
Copy-Item -LiteralPath (Join-Path $projectRoot 'favicon.png') -Destination $outputDir
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/next-engine.md') -Destination (Join-Path $outputDir 'ReadMe.md')
$readmePath = Join-Path $outputDir 'ReadMe.md'
[IO.File]::WriteAllText($readmePath, [IO.File]::ReadAllText($readmePath).Replace('src="../favicon.png"', 'src="favicon.png"'), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/next-interface.png') -Destination $outputDir
$packageFiles = Get-ChildItem -LiteralPath $outputDir | Where-Object { $_.Name -notin @('Settings.json', 'Settings.json.tmp', 'SystemCursorLease.json', 'SystemCursorLease.json.tmp', 'Renderer.log', 'Renderer.log.old', '.installed') }
Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath "$outputDir.zip" -Force
Write-Output "$outputDir.zip"
