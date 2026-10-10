param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ExpectedManifestHash,
    [Parameter(Mandatory)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:ProgramFiles 'Candlelight\0.2.6.secure'
$atRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs'
$mainName = 'Candlelight_ColorFilter_v1'
$secureName = 'Candlelight_ColorFilterSecure_v1'
$registered = @()
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required for AT registration.' }
    $package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')
    $manifestPath = Join-Path $package 'SecureDesktopPackage.json'
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ExpectedManifestHash) { throw 'The reviewed package manifest changed.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($file in $manifest.Files.PSObject.Properties) {
        $path = [IO.Path]::GetFullPath((Join-Path $package $file.Name))
        if (!$path.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $path).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Invalid package path.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) { throw 'A prepared file changed.' }
    }
    foreach ($name in @('Candlelight.Next.exe', 'Candlelight.Secure.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $package $name)
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $manifest.CertificateThumbprint) { throw 'The existing trusted signature did not validate.' }
    }
    if (Test-Path -LiteralPath $destination) { throw 'The protected destination already exists.' }
    foreach ($name in @($mainName, $secureName)) {
        if (Test-Path -LiteralPath (Join-Path $atRoot $name)) { throw 'The AT registration already exists.' }
    }
    function Protect-Directory([string]$Path) {
        if (Test-Path -LiteralPath $Path) {
            if ((Get-Item -LiteralPath $Path).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'A protected directory must not be a link.' }
        } else { New-Item -ItemType Directory -Path $Path | Out-Null }
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
        foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        Set-Acl -LiteralPath $Path -AclObject $acl
    }
    $parent = Split-Path -Parent $destination
    if ((Get-Item -LiteralPath $parent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'The installation parent must not be a link.' }
    Protect-Directory $destination
    foreach ($file in $manifest.Files.PSObject.Properties) {
        $target = Join-Path $destination $file.Name
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $package $file.Name) -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Value) { throw 'Installed hash mismatch.' }
    }
    Copy-Item -LiteralPath $manifestPath -Destination $destination
    $diagnosticParent = Join-Path $env:ProgramData 'Candlelight'
    Protect-Directory $diagnosticParent
    Protect-Directory (Join-Path $diagnosticParent 'SecureDesktop')
    $resource = Join-Path $destination 'Candlelight.Engine.dll'
    foreach ($name in @($mainName, $secureName)) {
        $key = Join-Path $atRoot $name
        New-Item -Path $key | Out-Null
        $registered += $key
        $secure = $name -eq $secureName
        $exeName = if ($secure) { 'Candlelight.Secure.exe' } else { 'Candlelight.Next.exe' }
        $nameId = if ($secure) { 103 } else { 101 }
        $descriptionId = if ($secure) { 104 } else { 102 }
        $values = @{
            ApplicationName = ('@' + $resource + ',-' + $nameId)
            Description = ('@' + $resource + ',-' + $descriptionId)
            ATExe = $exeName
            StartExe = (Join-Path $destination $exeName)
            StartParams = $(if ($secure) { '--secure-desktop' } else { '--hidden' })
            SimpleProfile = 'Magnifier'
            Profile = '<HCIModel><Accommodation type="mild vision" /></HCIModel>'
        }
        if (!$secure) { $values.SecureDesktopAccommodation = $secureName }
        foreach ($valueName in $values.Keys) { New-ItemProperty -LiteralPath $key -Name $valueName -Value $values[$valueName] -PropertyType String | Out-Null }
        foreach ($valueName in @('CopySettingsToLockedDesktop', 'PassiveAutoStartBehavior')) { New-ItemProperty -LiteralPath $key -Name $valueName -Value 1 -PropertyType DWord | Out-Null }
        New-ItemProperty -LiteralPath $key -Name TerminateOnDesktopSwitch -Value 0 -PropertyType DWord | Out-Null
    }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $true; Destination = $destination; ExistingCertificateReused = $true; Registration = $mainName; SecureRegistration = $secureName } | ConvertTo-Json))
    exit 0
} catch {
    foreach ($key in $registered) { Remove-Item -LiteralPath $key -Recurse -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllText($ResultPath, (@{ Success = $false; Error = $_.Exception.Message; Detail = $_.ToString() } | ConvertTo-Json))
    exit 1
}
