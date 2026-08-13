# NEXUS ESP32-P4 Display Firmware

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Development](https://img.shields.io/badge/development-v2.1.1-f3b61f)](release.json)
[![Protocol](https://img.shields.io/badge/protocol-2.0-6dff39)](COMPATIBILITY.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS configures an ESP32-P4-Function-EV-Board and a 1024 × 600 MIPI-DSI display kit as a Windows host telemetry display. The firmware receives newline-delimited JSON through the ESP32-P4 native USB Serial/JTAG interface and uses LVGL to present CPU, GPU, memory, network, fan, temperature, power, and the energy accumulated during the current desktop-application session.

This is the firmware development branch. The current version is **2.1.1** and is not a stable deployment baseline. Stable firmware is on [`firmware-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-stable), and the corresponding Windows development source is on [`desktop-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-dev).

v2.1.1 adds bidirectional device authentication, runtime compatibility-range checks, and a per-session nonce. It advances the wire protocol to 2.0 and is classified as `coordinated-breaking`. Desktop v2.1.1 is required; v1.x/v2.x combinations are rejected explicitly.

## Runtime behavior

- Host telemetry refreshes at 1 Hz, and each chart retains the latest 60 samples.
- The display enters `WAIT` after 3.5 seconds without a valid frame. After 15 seconds without activity, it disables the backlight and stops LVGL, touch, and MIPI-DSI; a new handshake restores the display.
- Disconnecting the serial link does not reset session energy; restarting the desktop application starts a new accumulation period.
- When Wi-Fi and Bluetooth are unused, the onboard ESP32-C6 remains in reset and the unused speaker amplifier is disabled.
- Missing sensor values are transmitted as `null` and displayed as `--`; unknown values are never substituted with zero.
- The USB link uses ESP32-P4 native USB Serial/JTAG and does not depend on a CH340 driver or a fixed COM number.
- A random-nonce bidirectional handshake identifies the device; an open or unique serial port is never accepted as proof of identity.

## Hardware connection

The firmware targets ESP32-P4-Function-EV-Board v1.4/v1.5 with the EK79007 1024 × 600 display kit. Remove all power before making the following connections:

| LCD adapter | ESP32-P4-Function-EV-Board |
| --- | --- |
| J3 | MIPI-DSI connector; insert the ribbon cable in the reversed orientation specified by the board guide |
| J6 `RST_LCD` | J1 `GPIO27` |
| J6 `PWM` | J1 `GPIO26` |
| J6 `5V` / `GND` | J1 `5V` / `GND`, or power the LCD adapter separately through its J1 USB connector |

See [docs/HARDWARE.md](docs/HARDWARE.md) for mechanical assembly, power boundaries, and inspection steps. The connection mapping is based on Espressif's official [ESP32-P4-Function-EV-Board v1.4 User Guide](https://docs.espressif.com/projects/esp-dev-kits/en/latest/esp32p4/esp32-p4-function-ev-board/user_guide_v1.4.html).

## Build

Requirements: ESP-IDF 5.5.x, Python, and ESP-IDF Component Manager. The dependency lock file pins the board BSP to 5.2.3 and LVGL to 9.5.0.

```powershell
idf.py build
```

ESP-IDF dependency paths can exceed the Windows path limit when the repository is nested deeply. From an initialized ESP-IDF shell, run `./tools/build-windows.ps1`; it uses a temporary short drive mapping and a separate `build-short` directory, then removes the mapping without changing the system-wide long-path policy. Pass `-BuildDirectory build-short` to `package-release.ps1` when packaging that output.

Flashing commands and prebuilt-image offsets are documented in [docs/FLASHING.md](docs/FLASHING.md). Stable artifacts are available from [GitHub Releases](https://github.com/YyyUuu114/nexus-esp32p4-display/releases).

## Versioning and compatibility

Product versions use `MAJOR.LINE.CHANNEL`: stable releases use `CHANNEL=0`, while development releases use `CHANNEL=1..9`. The wire protocol is versioned independently as `WIRE_MAJOR.WIRE_REVISION`. Whether the peer must be updated is declared explicitly in each release's `release.json` and must not be inferred from the product version alone.

See [COMPATIBILITY.md](COMPATIBILITY.md) for the normative rules, [compatibility.json](compatibility.json) for the machine-readable policy, and [release.json](release.json) for current release metadata. v2.1.1 is the development baseline for protocol 2.0 and requires both endpoints to be upgraded; the stable v1.1.0 baseline remains unchanged.

## License and security

Original project code is licensed under the Apache License 2.0. License and pinned-version information for fonts, the BSP, LVGL, and other dependencies is recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an individual developer's learning and research project provided without security or damage warranty; review the complete bilingual risk notice and private reporting instructions in [SECURITY.md](SECURITY.md).
