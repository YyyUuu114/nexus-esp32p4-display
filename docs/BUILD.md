# Reproducible Windows Build

## Requirements

- Windows 10 or Windows 11 x64.
- .NET 10 SDK.
- PowerShell 7.4 or later.
- HTTPS access to the official LibreHardwareMonitor GitHub Release and Microsoft NuGet feed when caches are empty.

## Dependency restore and tests

```powershell
./tools/restore-lhm.ps1
dotnet restore ./NexusDisplayAgent.csproj
dotnet build ./NexusDisplayAgent.csproj -c Release --no-restore
dotnet run --project ./tests/NexusDisplay.Tests.csproj -c Release
```

`restore-lhm.ps1` downloads only LibreHardwareMonitor v0.9.6 `LibreHardwareMonitor.NET.10.zip`, verifies the pinned archive and primary-DLL SHA-256 values, and extracts into ignored `vendor/`. Because that archive contains reference, Unix, and Windows assemblies with identical identities, the script also creates an isolated Windows runtime directory and verifies the selected `System.IO.Ports` and `System.Management` hashes. The console tests require no external test framework and cover product-version ordering, energy monotonicity, handshake validation, Windows serial enumeration, ECDSA verification, and the published signed envelope.

Warnings are errors. Deterministic compilation and the single project `<Version>` establish assembly and file version 2.1.2. MSBuild fails the build if the output does not contain the pinned Windows serial and WMI runtime assemblies.

## Update-signing key

Create a maintainer key once, outside the repository:

```powershell
./tools/new-update-signing-key.ps1 -OutputPath D:\secure\nexus-update-ecdsa-p256.pem
```

The script prints only the public SubjectPublicKeyInfo value and applies a user-only ACL to the private file. Compare the public value with `package/update-trust.json`; never commit, log, archive, or transmit the private PEM.

## Release package

```powershell
./tools/build-release.ps1 `
  -DotNetPath dotnet `
  -SigningKeyPath D:\secure\nexus-update-ecdsa-p256.pem
```

The script validates metadata and source hygiene, restores the pinned dependency, publishes a self-contained `win-x64` single file, assembles the portable folder, creates a versioned ZIP, signs an update envelope, and writes `SHA256SUMS.txt`. It refuses a private key whose public half differs from `package/update-trust.json`.

If a trusted Windows code-signing certificate is available in `Cert:\CurrentUser\My`, pass `-AuthenticodeCertificateThumbprint`. This optional step signs the executable before ZIP creation; it does not replace the mandatory project update signature.

Copy the generated envelope into `package/latest-update.json` only after reviewing its decoded payload and package hash. Commit source and the reviewed envelope to `desktop-dev` first, then create the declared GitHub prerelease tag and upload the exact ZIP, envelope, and checksum file. `vendor`, `bin`, `obj`, `artifacts`, executables, private keys, and ZIP files must remain outside branch history.
