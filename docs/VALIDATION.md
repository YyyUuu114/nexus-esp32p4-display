# Firmware v2.1.1 Development Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 2.1.1 |
| Release channel | development |
| Update classification | coordinated-breaking |
| Wire protocol | 2.0 |
| Target | ESP32-P4 |
| ESP-IDF | 5.5.2 |
| Board BSP | 5.2.3 |
| LVGL | 9.5.0 |
| Build date | 2026-08-13 |

## Verification summary

- The isolated public source passed `tools/validate-release.ps1 -Component firmware -Channel development`.
- A full Windows build completed through `tools/build-windows.ps1`, using a temporary short-path drive mapping and treating project warnings as errors.
- The application image is 1,105,856 bytes (`0x10DFC0`). The 5 MiB application partition retains `0x3F2040` bytes (79%).
- The bootloader image is `0x5E40` bytes. With the partition-table offset at `0x10000`, the bootloader region retains `0x81C0` bytes (58%).
- The release package includes individual images and a merged image for a complete erase-and-flash migration from protocol 1.x.

## Release image digests

| Image | Size | SHA-256 |
| --- | ---: | --- |
| `bootloader.bin` | 24,128 bytes | `A2BB1A747BEE90F83EFEFABF408E50A5A9B6D05E571E1F90F0BFD417EACB7300` |
| `partition-table.bin` | 3,072 bytes | `D40E1107BF7D2C46643FF2500373792EE0A9EF21C2AB0D4EEDB3386FF2C6B57F` |
| `nexus_display.bin` | 1,105,856 bytes | `B9F41C96AF6FA54B64A264E3B39C718D93B3C592DB0D3EE8E6BB8A3739E44129` |
| `nexus-firmware-full.bin` | 1,236,928 bytes | `0C4634E8F6BC5C842942533613C4EC26A71A4CCCBA0B6B2AD0DB3C5A6032B2E8` |
| `NEXUS-Firmware-ESP32P4-v2.1.1.zip` | 1,249,721 bytes | `DB9AC04C9F48470F1B6AB0A828D5B059478956E424D5781690D6AFE01E022F63` |

Generated binary artifacts are distributed through the matching GitHub prerelease and are intentionally excluded from branch history.
