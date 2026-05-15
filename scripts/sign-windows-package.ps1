param(
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [securestring]$CertificatePassword,
    [switch]$CreateDevCertificate,
    [switch]$NoTimestamp,
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot "artifacts"
$PublishDir = Join-Path $ArtifactsRoot "publish\$Runtime"
$PackageDir = Join-Path $ArtifactsRoot "package\Jieping-$Runtime"
$ZipPath = Join-Path $ArtifactsRoot "Jieping-$Runtime-$Version-portable.zip"

function Get-CodeSigningCertificate {
    if ($CertificatePath) {
        if (-not (Test-Path $CertificatePath)) {
            throw "Certificate path does not exist: $CertificatePath"
        }

        if ($CertificatePassword) {
            return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $CertificatePath,
                $CertificatePassword,
                [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
        }

        return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
    }

    if ($CertificateThumbprint) {
        $normalizedThumbprint = $CertificateThumbprint.Replace(" ", "")
        $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
            Select-Object -First 1

        if (-not $certificate) {
            throw "Code signing certificate was not found by thumbprint: $CertificateThumbprint"
        }

        return $certificate
    }

    if ($CreateDevCertificate) {
        $existing = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object {
                $_.Subject -eq "CN=Jieping Development Code Signing" -and
                $_.NotAfter -gt (Get-Date) -and
                $_.HasPrivateKey
            } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

        if ($existing) {
            return $existing
        }

        return New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=Jieping Development Code Signing" `
            -CertStoreLocation Cert:\CurrentUser\My `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears(1)
    }

    throw "Provide -CertificateThumbprint, -CertificatePath, or -CreateDevCertificate."
}

if (-not (Test-Path $PackageDir)) {
    throw "Package directory does not exist. Run scripts\package-windows.ps1 first."
}

$certificate = Get-CodeSigningCertificate
$signableExtensions = @(".exe", ".dll", ".ps1")
$filesToSign = @()

if (Test-Path $PublishDir) {
    $filesToSign += Get-ChildItem -LiteralPath $PublishDir -File |
        Where-Object { $signableExtensions -contains $_.Extension.ToLowerInvariant() }
}

$filesToSign += Get-ChildItem -LiteralPath $PackageDir -File -Recurse |
    Where-Object { $signableExtensions -contains $_.Extension.ToLowerInvariant() }
$filesToSign = $filesToSign | Sort-Object FullName -Unique

if ($filesToSign.Count -eq 0) {
    throw "No signable files were found in package artifacts."
}

foreach ($file in $filesToSign) {
    $signature = if (-not $NoTimestamp -and $TimestampServer) {
        Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $certificate -TimestampServer $TimestampServer
    }
    else {
        Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $certificate
    }

    if ($signature.Status -notin @("Valid", "UnknownError")) {
        throw "Signing failed for $($file.FullName): $($signature.StatusMessage)"
    }

    Write-Host "Signed: $($file.FullName) [$($signature.Status)]"
}

[GC]::Collect()
[GC]::WaitForPendingFinalizers()
Start-Sleep -Milliseconds 500

if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath -Force

$hash = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
$hashPath = "$ZipPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$($hash.Hash)  $(Split-Path -Leaf $ZipPath)" -Encoding ASCII

Write-Host "Signed package files with certificate: $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Archive: $ZipPath"
Write-Host "SHA256:  $hashPath"
