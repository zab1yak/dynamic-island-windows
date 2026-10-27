<#
.SYNOPSIS
  Builds and signs the identity-only package (packaging/identity) that gives DynamicIsland.exe package
  identity for the notification history (UserNotificationListener). Run once; rerun only when
  packaging/identity/AppxManifest.xml changes.

.DESCRIPTION
  1. Packs packaging/identity with makeappx.
  2. Creates a throwaway self-signed code-signing certificate "CN=DynamicIsland Identity" in the
     current user's store, signs the package with signtool and exports the PUBLIC certificate.
  3. Deletes the certificate and its private key again. Nobody keeps a key that could sign other
     packages; the shipped package never changes, so updates do not need a new signature.

  Output (committed): packaging/identity/DynamicIslandIdentity.msix and DynamicIslandIdentity.cer.
  A new run produces a new certificate, which users must trust again — so run it only when needed.

.PARAMETER BuildTools
  Folder with makeappx.exe and signtool.exe (Windows SDK, or the Microsoft.Windows.SDK.BuildTools NuGet package).
#>
param([Parameter(Mandatory)] [string]$BuildTools)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'packaging\identity'
$msix = Join-Path $source 'DynamicIslandIdentity.msix'
$cer = Join-Path $source 'DynamicIslandIdentity.cer'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("di-identity-" + [guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory $staging | Out-Null
try {
    Copy-Item (Join-Path $source 'AppxManifest.xml') $staging
    Copy-Item (Join-Path $source 'Assets') $staging -Recurse

    & (Join-Path $BuildTools 'makeappx.exe') pack /d $staging /p $msix /nv /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DynamicIsland Identity' `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(20) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    try {
        & (Join-Path $BuildTools 'signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint /s My $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
        Export-Certificate -Cert $cert -FilePath $cer -Type CERT | Out-Null
        "Signed with $($cert.Thumbprint)"
    }
    finally {
        # The private key must not outlive the signature.
        Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -Force
    }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
