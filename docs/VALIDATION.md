# Desktop v1.2.1 Development Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 1.2.1 |
| Release channel | development |
| Wire protocol | 1.1 |
| Target framework | `net10.0-windows` |
| Runtime identifier | `win-x64` |
| .NET SDK | 10.0.302 |
| Bundled .NET runtime | 10.0.10 |
| LibreHardwareMonitor | 0.9.6 |
| Build date | 2026-08-09 |

## Build result

The isolated release source completed `dotnet publish` with deterministic compilation and warnings treated as errors. The output is a self-contained single-file Windows executable; the target computer does not require a separately installed .NET runtime.

The pinned LibreHardwareMonitor archive and primary library passed their SHA-256 checks before compilation. The local validation ZIP was inspected to confirm the executable, launch and startup scripts, user instructions, development channel configuration, compatibility metadata, project license, third-party notices, and MPL-2.0 license are present. Debug symbol files are excluded. The development channel intentionally has no online manifest until a reviewed development asset is published.

## Release asset

| Asset | Size | SHA-256 |
| --- | ---: | --- |
| `NEXUS-Display-Windows-x64.zip` | 48,992,553 bytes | `AC0CD0134E10AED904EA5315CF3E4115B169EAC542A3ECE061D984B44FF57711` |

This ZIP is a local validation artifact and is not attached to the stable v1.1.0 GitHub Release.
