param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [string]$Channel = "stable",
    [string]$UpdateBaseUrl,
    [switch]$GenerateManifest,
    [switch]$Sign,
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
$ProjectPath = Join-Path $RepoRoot "Jieping.App\Jieping.App.csproj"
$ArtifactsRoot = Join-Path $RepoRoot "artifacts"
$PublishDir = Join-Path $ArtifactsRoot "publish\$Runtime"
$PackageDir = Join-Path $ArtifactsRoot "package\Jieping-$Runtime"
$ZipFileName = "Jieping-$Runtime-$Version-portable.zip"
$ZipPath = Join-Path $ArtifactsRoot $ZipFileName
$ManifestPath = Join-Path $ArtifactsRoot "update-manifest.json"

if (Test-Path $PublishDir) {
    Remove-Item -LiteralPath $PublishDir -Recurse -Force
}

if (Test-Path $PackageDir) {
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}

if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

if (Test-Path $ManifestPath) {
    Remove-Item -LiteralPath $ManifestPath -Force
}

New-Item -ItemType Directory -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Path $PackageDir | Out-Null

dotnet publish $ProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDir `
    /p:Version=$Version `
    /p:FileVersion=$Version.0 `
    /p:AssemblyVersion=$Version.0 `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=embedded

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $PackageDir -Recurse -Force

$installScript = @'
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Jieping",
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"

$SourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath = Join-Path $SourceDir "Jieping.App.exe"
if (-not (Test-Path $ExePath)) {
    throw "Jieping.App.exe was not found beside this installer script."
}

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force

if (-not $NoShortcut) {
    $ProgramsDir = [Environment]::GetFolderPath("Programs")
    $ShortcutPath = Join-Path $ProgramsDir "Jieping.lnk"
    $Shell = New-Object -ComObject WScript.Shell
    $Shortcut = $Shell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = Join-Path $InstallDir "Jieping.App.exe"
    $Shortcut.WorkingDirectory = $InstallDir
    $Shortcut.Description = "Jieping screen recorder"
    $Shortcut.Save()
}

Write-Host "Jieping installed to $InstallDir"
'@

$uninstallScript = @'
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Jieping"
)

$ErrorActionPreference = "Stop"

$ProgramsDir = [Environment]::GetFolderPath("Programs")
$ShortcutPath = Join-Path $ProgramsDir "Jieping.lnk"
if (Test-Path $ShortcutPath) {
    Remove-Item -LiteralPath $ShortcutPath -Force
}

if (Test-Path $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

Write-Host "Jieping uninstalled from $InstallDir"
'@

$packageReadme = @'
# Jieping Windows Package

This package contains a self-contained Windows x64 build of Jieping.

## Install

Run PowerShell from this folder:

```powershell
.\install.ps1
```

The installer copies the app to `%LOCALAPPDATA%\Jieping` and creates a Start Menu shortcut.

## Uninstall

```powershell
.\uninstall.ps1
```

## Runtime Notes

- Windows 10 1903 or later is recommended.
- FFmpeg and FFprobe must be available on PATH for recording, trimming, and GIF export.
'@

Set-Content -LiteralPath (Join-Path $PackageDir "install.ps1") -Value $installScript -Encoding UTF8
Set-Content -LiteralPath (Join-Path $PackageDir "uninstall.ps1") -Value $uninstallScript -Encoding UTF8
Set-Content -LiteralPath (Join-Path $PackageDir "README.txt") -Value $packageReadme -Encoding UTF8

Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath -Force

if ($Sign) {
    $signScript = Join-Path $ScriptRoot "sign-windows-package.ps1"
    $signArguments = @{
        Runtime = $Runtime
        Version = $Version
    }

    if ($CertificateThumbprint) {
        $signArguments.CertificateThumbprint = $CertificateThumbprint
    }

    if ($CertificatePath) {
        $signArguments.CertificatePath = $CertificatePath
    }

    if ($CertificatePassword) {
        $signArguments.CertificatePassword = $CertificatePassword
    }

    if ($CreateDevCertificate) {
        $signArguments.CreateDevCertificate = $true
    }

    if ($NoTimestamp) {
        $signArguments.NoTimestamp = $true
    }

    if ($TimestampServer) {
        $signArguments.TimestampServer = $TimestampServer
    }

    & $signScript @signArguments
}

if (-not (Test-Path "$ZipPath.sha256")) {
    $hash = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
    Set-Content -LiteralPath "$ZipPath.sha256" -Value "$($hash.Hash)  $ZipFileName" -Encoding ASCII
}

if ($GenerateManifest) {
    if ([string]::IsNullOrWhiteSpace($UpdateBaseUrl)) {
        throw "UpdateBaseUrl is required when GenerateManifest is set."
    }

    $normalizedBaseUrl = $UpdateBaseUrl.TrimEnd("/")
    $hash = (Get-Content -LiteralPath "$ZipPath.sha256" -Raw).Trim().Split(" ")[0]
    $manifest = [ordered]@{
        schemaVersion = 1
        appId = "Jieping"
        channel = $Channel
        version = $Version
        publishedAt = (Get-Date).ToUniversalTime().ToString("o")
        runtime = $Runtime
        packageType = "portable-zip"
        fileName = $ZipFileName
        packageUrl = "$normalizedBaseUrl/$ZipFileName"
        downloadUrl = "$normalizedBaseUrl/$ZipFileName"
        sha256 = $hash
        sizeBytes = (Get-Item -LiteralPath $ZipPath).Length
        minimumAppVersion = "0.1.0"
        mandatory = $false
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
    Write-Host "Manifest:  $ManifestPath"
}

Write-Host "Published: $PublishDir"
Write-Host "Package:   $PackageDir"
Write-Host "Archive:   $ZipPath"
