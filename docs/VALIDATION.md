# Firmware v1.1.0 Validation Record

## Build identity

| Item | Value |
| --- | --- |
| Product version | 1.1.0 |
| Release channel | stable |
| Wire protocol | 1.1 |
| Target | ESP32-P4 |
| ESP-IDF | 5.5.2 |
| Board BSP | 5.2.3 |
| LVGL | 9.5.0 |
| Build date | 2026-08-09 |

## Build result

The isolated release source completed a clean `idf.py set-target esp32p4` and `idf.py build` with component warnings promoted to errors. The application image is 1,095,696 bytes (`0x10B810`). The 5 MiB application partition retains 4,147,184 bytes (`0x3F47F0`, approximately 79%).

## Release image digests

| Image | Size | SHA-256 |
| --- | ---: | --- |
| `bootloader.bin` | 24,128 bytes | `80B2FF0A615F26AA6BD58116335F59418D33119275C16AB728D5FB65557800BC` |
| `partition-table.bin` | 3,072 bytes | `1EDAD21A69D0791CB6FE8A4F47EA7C64CFD302031F8DB2C9F68CDC9568CA124C` |
| `nexus_display.bin` | 1,095,696 bytes | `FBBC9DDBC82AE040720B97B0384B719D7F2B0AF7D31557B67D3F10CE9094849E` |

The binary images are distributed through the v1.1.0 GitHub Release and are intentionally excluded from branch history.
