# NEXUS Display for Windows

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Development](https://img.shields.io/badge/development-v1.2.1-f3b61f)](release.json)
[![Protocol](https://img.shields.io/badge/protocol-1.1-6dff39)](COMPATIBILITY.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4)](docs/BUILD.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS Display is the Windows tray collector for the ESP32-P4 host telemetry display. It reads CPU, GPU, memory, network, fan, temperature, and power data, sends telemetry at 1 Hz through ESP32-P4 native USB Serial/JTAG, and accumulates available CPU and GPU power readings for the current application session.

This is the desktop development branch. The current version is **1.2.1** and is not a stable deployment baseline. Stable desktop source is on [`desktop-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-stable), and firmware development source is on [`firmware-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-dev).

v1.2.1 establishes the next development channel without changing wire protocol 1.1. It is classified as `single-endpoint`, remains compatible with firmware v1.1.0, and does not require a firmware update.

## Development quick start

1. Produce a development package by following “Build from source.”
2. Extract the complete ZIP. Do not run the application from inside an archive manager.
3. Double-click `NEXUS Display\01-启动程序.cmd` or `Nexus Display.exe`.
4. To enable optional login startup, double-click `02-启用开机自启.cmd`; use `03-取消开机自启.cmd` to disable it.

Production environments should use the stable [v1.1.0 release](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0).

The package includes the .NET runtime, so the target computer does not need the .NET SDK. Reading some motherboard sensors requires administrator privileges and therefore produces a Windows UAC prompt when started manually.

## Connection and drivers

- The application first identifies Espressif USB Serial/JTAG devices by USB VID `303A` and PID `1001`, then resolves the currently assigned COM number.
- Windows assigns COM numbers dynamically. The application does not hard-code a port, so moving the device to another computer or USB connector does not prevent automatic discovery.
- This data path uses ESP32-P4 native USB rather than CH340 and does not require a CH340 driver. A CH340 driver is required only if the user substitutes an external CH340 USB-to-UART adapter.
- If the system exposes only one serial port, the application may use it as a compatibility fallback. When multiple non-target ports are present, it does not connect to an arbitrary port.

See [docs/SERIAL_DISCOVERY.md](docs/SERIAL_DISCOVERY.md) for the complete selection algorithm and limitations.

## Resource-efficiency design

- Sensors and network counters update once per second; the application does not perform high-frequency polling.
- While disconnected, serial discovery runs every three seconds; exceptional reconnects use a 2.5-second backoff.
- The application creates no internet connection unless the user explicitly requests an update check.
- Logs use short single-line records, rotate at 256 KiB, retain two history files, and remove logs older than 14 days.
- A single-instance mutex prevents repeated launches from creating duplicate collectors.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for design boundaries.

## Updates and version compatibility

The tray menu's update command reads an HTTPS manifest, verifies the release channel and SHA-256 digest, then performs replacement and restart. Stable builds read only the `stable` manifest, while development builds read only the `development` manifest, enforcing the `.0` stable and `.1..9` development version convention.

Product and wire-protocol rules are defined in [COMPATIBILITY.md](COMPATIBILITY.md). v1.1.0 is the initial paired baseline, so the first deployment should combine desktop v1.1.0 with firmware v1.1.0. A breaking coordinated update identifies the required firmware version in the confirmation dialog.

## Build from source

Requirements: .NET 10 SDK and PowerShell 7.4 or later.

```powershell
./tools/restore-lhm.ps1
./tools/build-release.ps1
```

The restore script downloads only the official LibreHardwareMonitor v0.9.6 asset and verifies its pinned SHA-256 digest. See [docs/BUILD.md](docs/BUILD.md) for complete instructions.

## License and security

Original project code is licensed under the Apache License 2.0. Versions, sources, and licenses for LibreHardwareMonitor, .NET, and other dependencies are recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an individual developer's learning and research project provided without security or damage warranty; review the complete bilingual risk notice and private reporting instructions in [SECURITY.md](SECURITY.md).
