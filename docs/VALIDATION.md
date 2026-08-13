# Desktop v2.1.1 Development Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 2.1.1 |
| Release channel | development |
| Update classification | coordinated-breaking |
| Wire protocol | 2.0 |
| Target framework | `net10.0-windows` |
| Runtime identifier | `win-x64` |
| .NET SDK | 10.0.302 |
| LibreHardwareMonitor | 0.9.6 |
| Update signature | ECDSA P-256 / SHA-256, key `nexus-dev-2026-01` |
| Build date | 2026-08-13 |

## Verification summary

- Release compilation completed with zero warnings and zero errors.
- The standalone core test executable passed version ordering, energy monotonicity, strict handshake, signature tamper detection, published-envelope verification, and archive traversal rejection.
- `tools/validate-release.ps1 -Component desktop -Channel development` passed source hygiene, metadata, branch, protected-install, startup-target, update-signature, rollback, test, and CI checks.
- LibreHardwareMonitor 0.9.6 was restored from the pinned archive and verified before compilation.
- The output is a self-contained, single-file Windows x64 executable. A separate .NET runtime is not required on the target computer.
- The update package is authenticated by the project's pinned ECDSA update key. This development build does not carry a commercial Authenticode certificate, so Windows may identify the executable as an unknown publisher.

## Release assets

| Asset | Size | SHA-256 |
| --- | ---: | --- |
| `NEXUS-Display-Windows-x64-v2.1.1.zip` | 48,997,960 bytes | `D08A18B05101FD49DDAF89BF2E51EDECAF9C7F9EB35B008D4CDF9C5C5C267B77` |
| `NEXUS-Display-Windows-x64-v2.1.1.nexus-update.json` | 875 bytes | `4FA6F24F34806A2EBAD35AF80E752A75970FBF7E6758B871512C258F3F4D4D48` |

Binary artifacts are distributed through the matching GitHub prerelease and are intentionally excluded from branch history.
