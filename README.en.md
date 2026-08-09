# NEXUS ESP32-P4 Display Firmware

[![简体中文](https://img.shields.io/badge/语言-简体中文-00d9ff)](README.md)
[![English](https://img.shields.io/badge/Language-English-6dff39)](README.en.md)

[![Release](https://img.shields.io/badge/release-v1.1.0-00d9ff)](https://github.com/YyyUuu114/nexus-esp32p4-display/releases/tag/v1.1.0)
[![Protocol](https://img.shields.io/badge/protocol-1.1-6dff39)](COMPATIBILITY.md)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

NEXUS configures an ESP32-P4-Function-EV-Board and a 1024 × 600 MIPI-DSI display kit as a Windows host telemetry display. The firmware receives newline-delimited JSON through the ESP32-P4 native USB Serial/JTAG interface and uses LVGL to present CPU, GPU, memory, network, fan, temperature, power, and the energy accumulated during the current desktop-application session.

This is the stable firmware branch. The current version is **1.1.0**. The corresponding stable Windows source is on [`desktop-stable`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-stable); development takes place on [`firmware-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/firmware-dev) and [`desktop-dev`](https://github.com/YyyUuu114/nexus-esp32p4-display/tree/desktop-dev).

## Runtime behavior

- Host telemetry refreshes at 1 Hz, and each chart retains the latest 60 samples.
- The display enters `WAIT` after 3.5 seconds without a valid frame. After 15 seconds, it disables the backlight and pauses data-area refresh. The next valid frame restores the display immediately.
- Disconnecting the serial link does not reset session energy; restarting the desktop application starts a new accumulation period.
- When Wi-Fi and Bluetooth are unused, the onboard ESP32-C6 remains in reset and the unused speaker amplifier is disabled.
- Missing sensor values are transmitted as `null` and displayed as `--`; unknown values are never substituted with zero.
- The USB link uses ESP32-P4 native USB Serial/JTAG and does not depend on a CH340 driver or a fixed COM number.

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
idf.py set-target esp32p4
idf.py build
```

Flashing commands and prebuilt-image offsets are documented in [docs/FLASHING.md](docs/FLASHING.md). Stable artifacts are available from [GitHub Releases](https://github.com/YyyUuu114/nexus-esp32p4-display/releases).

## Versioning and compatibility

Product versions use `MAJOR.LINE.CHANNEL`: stable releases use `CHANNEL=0`, while development releases use `CHANNEL=1..9`. The wire protocol is versioned independently as `WIRE_MAJOR.WIRE_REVISION`. Whether the peer must be updated is declared explicitly in each release's `release.json` and must not be inferred from the product version alone.

See [COMPATIBILITY.md](COMPATIBILITY.md) for the normative rules, [compatibility.json](compatibility.json) for the machine-readable policy, and [release.json](release.json) for current release metadata. v1.1.0 is the initial paired baseline; the first deployment requires firmware v1.1.0 and desktop v1.1.0 together.

## License and security

Original project code is licensed under the Apache License 2.0. License and pinned-version information for fonts, the BSP, LVGL, and other dependencies is recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an individual developer's learning and research project provided without security or damage warranty; review the complete bilingual risk notice and private reporting instructions in [SECURITY.md](SECURITY.md).
