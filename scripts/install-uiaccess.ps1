param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ExpectedManifestHash,
    [switch]$VerifyOnly
)

# Run elevated only after approval of this local certificate and Program Files
# installation. Does not disable Windows policies or launch an elevated renderer.
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$manifestPath = Join-Path $package 'UIAccessPackage.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ExpectedManifestHash) { throw 'Package manifest changed.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Version -ne '0.2.5') { throw 'Unexpected package version.' }
foreach ($file in $manifest.Files.PSObject.Properties) {
    if ([IO.Path]::IsPathRooted($file.Name) -or $file.Name.Contains(':') -or ($file.Name -split '[\\/]' | Where-Object { $_ -in @('', '.', '..') })) { throw 'Unsafe package file name.' }
    if ((Get-FileHash -LiteralPath (Join-Path $package $file.Name) -Algorithm SHA256).Hash -ne $file.Value) { throw ('Package changed: ' + $file.Name) }
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $package 'Candlelight.Local.cer'))
if ($certificate.Thumbprint -ne $manifest.CertificateThumbprint) { throw 'Unexpected signing certificate.' }
if ($VerifyOnly) { Write-Output ('PASS: verified all ' + @($manifest.Files.PSObject.Properties).Count + ' package hashes and certificate; no system changes.'); return }
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required for this installation.' }
$destination = Join-Path $env:ProgramFiles 'Candlelight\0.2.5.uiaccess'
if (Test-Path -LiteralPath $destination) { throw 'Destination already exists. Review it before installing again.' }
$parent = Split-Path -Parent $destination
if ((Test-Path -LiteralPath $parent) -and (Get-Item -LiteralPath $parent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Installation parent must not be a link.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# A protected ACL is required for UIAccess; never put this build in a user-writable
# directory. Standard users retain read/execute only, including child DLLs.
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
foreach ($entry in @(@('S-1-5-18','FullControl'), @('S-1-5-32-544','FullControl'), @('S-1-5-32-545','ReadAndExecute'))) {
    $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry[0]), [Security.AccessControl.FileSystemRights]$entry[1], [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit', [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $parent -AclObject $acl
Set-Acl -LiteralPath $destination -AclObject $acl
$trustPath = 'Cert:\LocalMachine\Root\' + $certificate.Thumbprint
$trustAlreadyPresent = Test-Path -LiteralPath $trustPath
$imported = $false
try {
    foreach ($file in $manifest.Files.PSObject.Properties) {
        $target = Join-Path $destination $file.Name
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $package $file.Name) -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Value) { throw 'Installed package hash mismatch.' }
    }
    if (!$trustAlreadyPresent) {
        Import-Certificate -FilePath (Join-Path $destination 'Candlelight.Local.cer') -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
        $imported = $true
    }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $destination 'Candlelight.Next.exe')
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Windows did not validate the installed signature.' }
    Copy-Item -LiteralPath $manifestPath -Destination $destination
    Write-Output ('Installed signed accessibility build: ' + $destination)
    Write-Output ('Certificate rollback: Remove-Item -LiteralPath "' + $trustPath + '"')
} catch {
    if ($imported) { Remove-Item -LiteralPath $trustPath }
    # Retain copied files for inspection; they cannot launch with UIAccess without
    # the approved certificate. No unrelated certificate or directory is removed.
    throw
}
