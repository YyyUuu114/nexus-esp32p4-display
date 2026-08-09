# Reproducible Windows Build

## Requirements

- Windows 10 or Windows 11 x64.
- .NET 10 SDK.
- PowerShell 7.4 or later.
- HTTPS access to the official LibreHardwareMonitor GitHub Release and the Microsoft NuGet feed when dependencies are not already cached.

## Dependency restore

```powershell
./tools/restore-lhm.ps1
```

The script downloads only the v0.9.6 `LibreHardwareMonitor.NET.10.zip` asset, verifies the pinned package SHA-256, extracts into ignored `vendor/`, and verifies the primary library SHA-256. Use `-Force` to replace an existing restored directory.

## Compile

```powershell
dotnet build ./NexusDisplayAgent.csproj -c Release
```

Warnings are treated as errors. The project uses deterministic compilation and embeds the product version from the single `<Version>` property in `NexusDisplayAgent.csproj`.

## Portable release package

```powershell
./tools/build-release.ps1
```

The script validates release metadata, publishes a self-contained `win-x64` single-file executable, adds launch/startup/update documentation and licenses, creates `artifacts/NEXUS-Display-Windows-x64.zip`, and prints its SHA-256.

The source tree does not commit restored DLLs, `bin`, `obj`, `artifacts`, executables, or ZIP files. Release packages belong in GitHub Releases.
