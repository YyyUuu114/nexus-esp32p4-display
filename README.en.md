# NEXUS Display for Windows

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Development](https://img.shields.io/badge/development-v2.1.1-f3b61f)](release.json)
[![Protocol](https://img.shields.io/badge/protocol-2.0-6dff39)](COMPATIBILITY.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4)](docs/BUILD.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS Display is the Windows tray collector for the ESP32-P4 host telemetry display. At 1 Hz it reads CPU, GPU, memory, network, fan, temperature, and power data, sends telemetry through ESP32-P4 native USB Serial/JTAG, and estimates energy from available CPU and all-GPU power samples for the current process lifetime.

This is desktop development version **2.1.1**. It introduces bidirectional device negotiation and a protected update chain, advances the wire protocol to 2.0, and is classified as `coordinated-breaking`; firmware v2.1.1 is required. Stable desktop source remains on [`desktop-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-stable).

## Extract and double-click

1. Extract the complete release ZIP.
2. Double-click `NEXUS Display\01-启动程序.cmd` or `Nexus Display.exe`.
3. After one UAC consent, the application installs to `C:\Program Files\NEXUS Display\current` and runs from that protected location.
4. Optional login startup is enabled by `02-启用开机自启.cmd` and disabled by `03-取消开机自启.cmd`. Installation does not enable startup automatically.

The package includes the .NET runtime. Administrator access is used for protected installation and hardware sensors. Production environments should remain on the stable [v1.1.0 release](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0) until protocol 2.x completes stable validation.

## Connection and drivers

- The application resolves the dynamic COM number from Espressif USB Serial/JTAG VID `303A`, PID `1001`.
- Every candidate must pass a protocol-2.0 random-nonce handshake, including product identity, wire version, and bidirectional product-version ranges. The former “only serial port” fallback is removed.
- Runtime transport uses ESP32-P4 native USB and does not traverse a CH340 bridge, so no CH340 driver is required unless an external CH340 adapter is deliberately substituted.
- Moving the board between computers or USB connectors is supported; non-NEXUS and incompatible ports are closed.

See [docs/SERIAL_DISCOVERY.md](docs/SERIAL_DISCOVERY.md) and [PROTOCOL.md](PROTOCOL.md).

## Efficiency, logs, and energy

- Hardware and network counters update once per second. Disconnected discovery runs every three seconds without busy waiting.
- The displayed primary GPU is selected deterministically at process start, preventing instantaneous-load switching from changing the energy source.
- Energy integrates valid non-negative CPU and all-GPU power samples. Board disconnects do not reset it; process restart does.
- Compact single-line logs rotate at 256 KiB, retain two history files, and remove files older than seven days.
- No internet connection is created unless the user invokes update checking.

## Signed atomic updates

The updater reads only the source-pinned official HTTPS location. The payload must verify against the embedded ECDSA P-256 public key, the package URL must belong to this repository's GitHub Releases, and the ZIP must match the signed SHA-256 digest. A protected helper validates extraction paths and swaps `incoming`, `current`, and `rollback`; failure to keep the new version running restores the previous version.

This project signature establishes updater identity but is not equivalent to commercial Windows Authenticode reputation. The release script can apply Authenticode when a maintainer supplies a code-signing certificate; an unsigned development asset may still show “Unknown publisher.” See [docs/UPDATE_FORMAT.md](docs/UPDATE_FORMAT.md) and [SECURITY.md](SECURITY.md).

## Build from source

Requirements: .NET 10 SDK and PowerShell 7.4+.

```powershell
./tools/restore-lhm.ps1
dotnet build ./NexusDisplayAgent.csproj -c Release
dotnet run --project ./tests/NexusDisplay.Tests.csproj -c Release
./tools/build-release.ps1 -SigningKeyPath D:\secure\nexus-update-key.pem
```

The private key must remain outside the repository with restricted access and must never be committed or packaged. See [docs/BUILD.md](docs/BUILD.md) for dependency and release details.

## License and security

Original project code is licensed under Apache License 2.0. Third-party sources and licenses are recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This individual-developer learning and research project is provided without security or software/hardware damage warranty; see [SECURITY.md](SECURITY.md) for the complete bilingual notice and private reporting instructions.
