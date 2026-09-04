# Desktop v2.1.3 Development Source Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 2.1.3 |
| Release channel | development |
| Update classification | single-endpoint |
| Compatible firmware | `>=2.1.1,<3.0.0` |
| Wire protocol | 2.0 |
| Target framework | `net10.0-windows` |
| Runtime identifier | `win-x64` |
| .NET SDK | 10.0.302 |
| LibreHardwareMonitor | 0.9.6 |
| Update signature | ECDSA P-256 / SHA-256, key `nexus-dev-2026-01` |
| Build date | 2026-09-04 |

## Verification summary

- Release compilation completed with zero warnings and zero errors.
- The standalone Windows core test executable passed version ordering, energy monotonicity, strict handshake, Windows serial-port enumeration, signature tamper detection, published-envelope verification, archive traversal rejection, and a blocked-provider simulation proving that transport continues independently.
- `tools/validate-release.ps1 -Component desktop -Channel development -SkipPublishedEnvelope` passed source hygiene, metadata, branch, protected-install, startup-target, rollback, runtime-binding, test, and CI checks. Exact envelope-to-version validation remains mandatory when a v2.1.3 package is published.
- LibreHardwareMonitor 0.9.6 was restored from the pinned archive. The isolated Windows `System.IO.Ports` and `System.Management` implementations were selected by hash after excluding the same-identity reference and Unix implementations.
- The final self-contained single-file executable completed `--runtime-self-test`; inspection of its extracted runtime confirmed the pinned Windows serial and WMI hashes. A separate .NET runtime is not required on the target computer.
- The update package is authenticated by the project's pinned ECDSA update key. This development build does not carry a commercial Authenticode certificate, so Windows may identify the executable as an unknown publisher.

## Last published development assets

| Asset | Size | SHA-256 |
| --- | ---: | --- |
| `NEXUS-Display-Windows-x64-v2.1.2.zip` | 49,001,783 bytes | `CB7B8A06812885E80412551D7BDDB7C90CB61AACCE02B760B04D9CE43447A655` |
| `NEXUS-Display-Windows-x64-v2.1.2.nexus-update.json` | 871 bytes | `7115D599720AE1297C3E45C91F996C51AD28C6A8F160418DA13469AFAC8AFB4C` |

Binary artifacts are distributed through the matching GitHub prerelease and are intentionally excluded from branch history.

No ESP32-P4 hardware was available for this source validation. CUDA-load behavior was verified offline with a deliberately blocked acquisition provider; physical USB disconnect/reconnect and live CUDA training remain required before promotion to a stable release.
