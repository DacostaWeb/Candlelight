param([Parameter(Mandatory)][string]$PackageDirectory)

# Preparation only: never installs trust, elevates, changes startup or launches
# the UIAccess application. The signing key is destroyed after this one build.
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$exe = Join-Path $package 'Candlelight.Next.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Publish the UIAccess build first.' }
$certificate = $null
try {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Candlelight local accessibility test 0.2.5' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddMonths(6) -HashAlgorithm SHA256
    $signature = Set-AuthenticodeSignature -FilePath $exe -Certificate $certificate -HashAlgorithm SHA256
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signature.Status -eq 'HashMismatch') { throw 'Signing failed.' }
    Export-Certificate -Cert $certificate -FilePath (Join-Path $package 'Candlelight.Local.cer') | Out-Null
    $files = @{}
    Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -ne 'UIAccessPackage.json' } | ForEach-Object { $files[$_.FullName.Substring($package.Length + 1)] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    $manifest = @{ Version = '0.2.5'; CertificateThumbprint = $certificate.Thumbprint; Files = $files }
    [IO.File]::WriteAllText((Join-Path $package 'UIAccessPackage.json'), ($manifest | ConvertTo-Json -Depth 5))
    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $package).FullName -DestinationPath ($package + '.zip') -Force
    Write-Output ('Prepared signed build. Certificate: ' + $certificate.Thumbprint + '. No trusted store changed.')
} finally {
    if ($certificate) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey }
}
