# Build and Flash Procedure

## Toolchain

- ESP-IDF 5.5.x; v1.1.0 is validated with ESP-IDF 5.5.2.
- ESP32-P4 target support and a data-capable USB cable.
- ESP-IDF Component Manager network access for the first dependency restore.

## Source build

Run the following commands from this branch root in an initialized ESP-IDF shell:

```powershell
idf.py set-target esp32p4
idf.py build
idf.py -p COMx flash monitor
```

Replace `COMx` with the port associated with the selected ESP32-P4 programming interface. Port numbering is assigned by Windows and is not stable between computers.

## Prebuilt release image

The v1.1.0 release contains three binary images. Flash them at the following offsets:

| Offset | File |
| --- | --- |
| `0x2000` | `bootloader.bin` |
| `0x8000` | `partition-table.bin` |
| `0x10000` | `nexus_display.bin` |

Example:

```powershell
esptool.py --chip esp32p4 -p COMx -b 460800 write_flash 0x2000 bootloader.bin 0x8000 partition-table.bin 0x10000 nexus_display.bin
```

Verify every downloaded file against `SHA256SUMS.txt` before flashing. Do not interrupt power while flash programming is active.

## USB driver behavior

The running application uses ESP32-P4 native USB Serial/JTAG. It does not require a CH340 driver. A separate USB-to-UART bridge, if selected for programming or debugging, may require the driver appropriate to the actual bridge controller; that requirement is independent of the application transport.
