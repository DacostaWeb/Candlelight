param(
    [Parameter(Mandatory)][string]$PreparedDirectory,
    [Parameter(Mandatory)][string]$ManifestHash,
    [Parameter(Mandatory)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $env:ProgramFiles 'Candlelight\0.2.6.transfer'
$destination = Join-Path $env:ProgramFiles 'Candlelight\0.2.7.startup'
$atRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs'
$backup = @{}
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required.' }
    $baselinePath = Join-Path $source 'SecureDesktopPackage.json'
    if ((Get-FileHash -LiteralPath $baselinePath).Hash -ne 'E047D8AB7295E625B4F22B69AEED612BA219912685364C34FCA2DB244D49A190') { throw 'The installed baseline changed.' }
    $manifest = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    foreach ($file in $manifest.Files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $source $file.Name)).Hash -ne $file.Value) { throw 'A baseline file changed.' }
    }
    $preparedPath = Join-Path $PreparedDirectory 'StartupUpdate.json'
    if ((Get-FileHash -LiteralPath $preparedPath).Hash -ne $ManifestHash) { throw 'The prepared manifest changed.' }
    $update = Get-Content -LiteralPath $preparedPath -Raw | ConvertFrom-Json
    $requiredFiles = @('Candlelight.Engine.dll','Candlelight.Engine.pdb','Candlelight.Next.dll','Candlelight.Next.pdb')
    if ($update.Version -ne '0.2.7' -or @($update.Files.PSObject.Properties).Count -ne 4) { throw 'Unexpected update manifest.' }
    foreach ($name in $requiredFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $PreparedDirectory $name)).Hash -ne $update.Files.$name) { throw 'A prepared assembly changed.' }
    }
    foreach ($registration in @('Candlelight_ColorFilter_v1','Candlelight_ColorFilterSecure_v1')) {
        $key = Join-Path $atRoot $registration
        $old = Get-ItemProperty -LiteralPath $key
        if (!$old.StartExe.StartsWith($source + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'AT registration no longer belongs to the baseline.' }
        $backup[$key] = @{ StartExe = $old.StartExe; ApplicationName = $old.ApplicationName; Description = $old.Description }
    }
    if (Test-Path -LiteralPath $destination) { throw 'The destination already exists.' }
    if ((Get-Item -LiteralPath (Split-Path -Parent $destination)).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'The installation parent must not be a link.' }
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $source) { Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse }
    foreach ($name in $requiredFiles) {
        Copy-Item -LiteralPath (Join-Path $PreparedDirectory $name) -Destination (Join-Path $destination $name) -Force
        $manifest.Files.$name = $update.Files.$name
    }
    $manifest.Hotfix = '0.2.7: early bounded black cover and ReadyToRun startup'
    $manifest | Add-Member -NotePropertyName Version -NotePropertyValue '0.2.7' -Force
    [IO.File]::WriteAllText((Join-Path $destination 'SecureDesktopPackage.json'), ($manifest | ConvertTo-Json -Depth 6))
    foreach ($file in $manifest.Files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $file.Name)).Hash -ne $file.Value) { throw 'Installed hash mismatch.' }
    }
    foreach ($exe in @('Candlelight.Next.exe','Candlelight.Secure.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $destination $exe)
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $manifest.CertificateThumbprint) { throw 'The existing signature failed.' }
    }
    $userRules = (Get-Acl -LiteralPath $destination).Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-5-32-545' -and $_.AccessControlType -eq 'Allow' }
    foreach ($rule in $userRules) { if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -ne 0) { throw 'Ordinary users can write to the protected runtime.' } }
    foreach ($key in $backup.Keys) {
        $secure = $key.EndsWith('Candlelight_ColorFilterSecure_v1')
        Set-ItemProperty -LiteralPath $key -Name StartExe -Value (Join-Path $destination $(if ($secure) { 'Candlelight.Secure.exe' } else { 'Candlelight.Next.exe' }))
        Set-ItemProperty -LiteralPath $key -Name ApplicationName -Value ('@' + (Join-Path $destination 'Candlelight.Engine.dll') + ',-' + $(if ($secure) { '103' } else { '101' }))
        Set-ItemProperty -LiteralPath $key -Name Description -Value ('@' + (Join-Path $destination 'Candlelight.Engine.dll') + ',-' + $(if ($secure) { '104' } else { '102' }))
    }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $true; Destination = $destination; ExistingCertificateReused = $true; Version = '0.2.7' } | ConvertTo-Json))
    exit 0
} catch {
    foreach ($key in $backup.Keys) { foreach ($name in $backup[$key].Keys) { Set-ItemProperty -LiteralPath $key -Name $name -Value $backup[$key][$name] -ErrorAction SilentlyContinue } }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $false; Error = $_.Exception.Message } | ConvertTo-Json))
    exit 1
}
