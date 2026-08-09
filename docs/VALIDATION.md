# Desktop v1.1.0 Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 1.1.0 |
| Release channel | stable |
| Wire protocol | 1.1 |
| Target framework | `net10.0-windows` |
| Runtime identifier | `win-x64` |
| .NET SDK | 10.0.302 |
| Bundled .NET runtime | 10.0.10 |
| LibreHardwareMonitor | 0.9.6 |
| Build date | 2026-08-09 |

## Build result

The isolated release source completed `dotnet publish` with deterministic compilation and warnings treated as errors. The output is a self-contained single-file Windows executable; the target computer does not require a separately installed .NET runtime.

The pinned LibreHardwareMonitor archive and primary library passed their SHA-256 checks before compilation. The release ZIP was inspected to confirm the executable, launch and startup scripts, user instructions, stable update-channel configuration, compatibility metadata, project license, third-party notices, and MPL-2.0 license are present. Debug symbol files are excluded.

## Release asset

| Asset | Size | SHA-256 |
| --- | ---: | --- |
| `NEXUS-Display-Windows-x64.zip` | 48,992,338 bytes | `F39C0D4009525A2EED5E16DB4885FE634414FAC538E769B0E3D73FA136169906` |

The release manifest at `update/latest.json` uses the same digest and points to the v1.1.0 GitHub Release asset.
