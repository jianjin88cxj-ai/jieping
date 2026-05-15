# Jieping

Jieping is a lightweight Windows desktop screen recorder. The current codebase is the Milestone 0 WPF foundation described in `docs/PRODUCT_DESIGN.md`.

## Requirements

- Windows 10 1903 or later.
- .NET SDK 8 or later with Windows Desktop support.

## Run

```powershell
dotnet run --project .\Jieping.App\Jieping.App.csproj
```

## Build

```powershell
dotnet build .\Jieping.slnx
```

## Package

Create a self-contained Windows x64 package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1
```

Create a signed package and update manifest:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1 -Version 0.1.1 -Sign -CreateDevCertificate -NoTimestamp -GenerateManifest -UpdateBaseUrl https://example.com/jieping/stable
```

Create and sign a package with a local development certificate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1 -Sign -CreateDevCertificate -NoTimestamp
```

Sign an existing package with a real certificate from the current user's certificate store:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-windows-package.ps1 -CertificateThumbprint <thumbprint>
```

Sign with a PFX:

```powershell
$password = Read-Host -AsSecureString
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-windows-package.ps1 -CertificatePath .\certs\release.pfx -CertificatePassword $password
```

Outputs:

- `artifacts\publish\win-x64\Jieping.App.exe`
- `artifacts\package\Jieping-win-x64\`
- `artifacts\Jieping-win-x64-<version>-portable.zip`
- `artifacts\Jieping-win-x64-<version>-portable.zip.sha256`
- `artifacts\update-manifest.json` when `-GenerateManifest` is used

The package includes `install.ps1` and `uninstall.ps1`. FFmpeg and FFprobe must be available on `PATH` for recording, trimming, and GIF export. Development self-signed signatures are expected to show an untrusted-chain status unless the certificate is explicitly trusted on the machine.

## Updates

Jieping supports a lightweight update check against a JSON manifest. The app compares the manifest version with its current version, can download the update package, verifies SHA256 when provided, and can open the verified package. It does not replace a running installation automatically.

## Diagnostics

Crash reports are disabled by default. When enabled in the app, Jieping saves local crash reports under `%LOCALAPPDATA%\Jieping\CrashReports`. Reports contain error messages and stack traces and are not uploaded automatically.

## Local Data

Jieping stores recent recording history in `%LOCALAPPDATA%\Jieping\recording-history.json`. The history contains file paths, artifact type, creation time, and short details for recent MP4/GIF artifacts. Clearing history removes this JSON history only; it does not delete recording files.

## Current Scope

- Region, Window, and Full Screen recording.
- FFmpeg-backed H.264 MP4 output.
- Optional system audio and microphone recording.
- Global hotkeys, countdown, pause/resume, cursor capture, click highlight, and watermark.
- Trimmed MP4 copies, GIF export, and in-session recording history.
- Persistent local recording history.
- Settings language selector for English and Chinese UI labels.
- Fixed-size polished main window layout with grouped recording, settings, post-processing, history, update, and diagnostics sections.
- Self-contained Windows x64 package, signing, and update manifest scripts.
- Local crash report opt-in.

Validation notes are tracked in `docs/VALIDATION_NOTES.md`.
