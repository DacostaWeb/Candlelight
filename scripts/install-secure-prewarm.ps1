param(
    [Parameter(Mandatory)][string]$PreparedDirectory,
    [Parameter(Mandatory)][string]$ManifestHash,
    [Parameter(Mandatory)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $env:ProgramFiles 'Candlelight\0.2.7.startup'
$destination = Join-Path $env:ProgramFiles 'Candlelight\0.2.8.prewarm2'
$atRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs'
$backup = @{}
$serviceCreated = $false
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required.' }
    if (Get-Service -Name 'Candlelight.SecureBroker' -ErrorAction SilentlyContinue) { throw 'The prewarming service already exists.' }
    $baselinePath = Join-Path $source 'SecureDesktopPackage.json'
    if ((Get-FileHash -LiteralPath $baselinePath).Hash -ne 'EF639B82B4D0AE942E760DFCF7D4239027AF0BC54EBDA057D439E104DC48E5F5') { throw 'The installed baseline changed.' }
    $manifest = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    foreach ($file in $manifest.Files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $source $file.Name)).Hash -ne $file.Value) { throw 'A baseline file changed.' }
    }
    $preparedPath = Join-Path $PreparedDirectory 'StartupUpdate.json'
    if ((Get-FileHash -LiteralPath $preparedPath).Hash -ne $ManifestHash) { throw 'The prepared manifest changed.' }
    $update = Get-Content -LiteralPath $preparedPath -Raw | ConvertFrom-Json
    $requiredFiles = @('Candlelight.Engine.dll','Candlelight.Engine.pdb','Candlelight.Next.dll','Candlelight.Next.pdb','Candlelight.SecureBroker.exe')
    if ($update.Version -ne '0.2.8' -or @($update.Files.PSObject.Properties).Count -ne 5) { throw 'Unexpected update manifest.' }
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
        $manifest.Files | Add-Member -NotePropertyName $name -NotePropertyValue $update.Files.$name -Force
    }
    $brokerImage = Join-Path $destination 'Candlelight.SecureBroker.exe'
    $manifest.Hotfix = '0.2.8: protected-desktop prewarming service'
    $manifest | Add-Member -NotePropertyName Version -NotePropertyValue '0.2.8' -Force
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
    New-Service -Name 'Candlelight.SecureBroker' -DisplayName 'Candlelight protected display preparation' -Description 'Prepares only the protected Candlelight renderer in the signed-in console session.' -BinaryPathName ('"' + $brokerImage + '" --secure-broker') -StartupType Automatic | Out-Null
    $serviceCreated = $true
    & sc.exe privs 'Candlelight.SecureBroker' 'SeTcbPrivilege/SeAssignPrimaryTokenPrivilege/SeIncreaseQuotaPrivilege' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict the service privileges.' }
    Start-Service -Name 'Candlelight.SecureBroker'
    $ready = $false
    $sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $sessionDiagnostic = Join-Path $env:ProgramData ('Candlelight\SecureDesktop\session-' + $sessionId + '.json')
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        try { $broker = Get-Content (Join-Path $env:ProgramData 'Candlelight\SecureDesktop\broker.json') -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch { continue }
        if ($broker.error -or $broker.state.error) { throw ('Prewarming failed: ' + $broker.error + $broker.state.error) }
        try { $native = Get-Content $sessionDiagnostic -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch { continue }
        if ($broker.state.prepared -and $native.pid -eq $broker.state.rendererPid -and !$native.active -and $native.engine.LocalSurfaces -ge 1 -and $native.engine.InputDesktopInactive) { $ready = $true; break }
    }
    if (!$ready) { throw 'The service did not prepare the inactive protected desktop.' }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $true; Destination = $destination; ExistingCertificateReused = $true; Version = '0.2.8' } | ConvertTo-Json))
    exit 0
} catch {
    if ($serviceCreated) {
        Stop-Service -Name 'Candlelight.SecureBroker' -ErrorAction SilentlyContinue
        & sc.exe delete 'Candlelight.SecureBroker' | Out-Null
    }
    foreach ($key in $backup.Keys) { foreach ($name in $backup[$key].Keys) { Set-ItemProperty -LiteralPath $key -Name $name -Value $backup[$key][$name] -ErrorAction SilentlyContinue } }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $false; Error = $_.Exception.Message } | ConvertTo-Json))
    exit 1
}
