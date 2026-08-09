# Hardware Integration

## Supported assembly

The v1.1.0 firmware is configured for the ESP32-P4-Function-EV-Board v1.4/v1.5 and the EK79007 1024 × 600 MIPI-DSI display kit. Board revisions with different connectors, reset routing, or panel timing require separate validation.

## Connection procedure

Disconnect all USB and external power before installing or removing a ribbon cable.

1. Secure the controller board to the center standoffs on the LCD adapter board without stressing either PCB.
2. Insert the LCD adapter board J3 ribbon into the ESP32-P4 MIPI-DSI connector in the reverse orientation specified by the official board guide. Lock both FFC latches fully.
3. Connect LCD adapter J6 `RST_LCD` to controller-board J1 `GPIO27`.
4. Connect LCD adapter J6 `PWM` to controller-board J1 `GPIO26`.
5. Power the LCD adapter using one of the following methods:
   - Preferred: connect a dedicated 5 V USB supply to LCD adapter J1.
   - Alternative: connect LCD adapter `5V` and `GND` to controller-board J1 `5V` and `GND` only when the controller supply has sufficient current capacity.
6. Connect the host computer to the ESP32-P4 native USB port used for USB Serial/JTAG. Use a data-capable USB cable.

Do not connect the LCD adapter's dedicated USB supply and an external 5 V feed unless the adapter documentation explicitly permits that power topology.

## Low-power board configuration

This application does not use wireless or audio output. At boot, ESP32-P4 GPIO54 holds the on-board ESP32-C6 enable/reset line low and GPIO53 holds the speaker amplifier disabled. These assignments are specific to the validated board revisions. Remove or revise this behavior before enabling ESP-Hosted, Wi-Fi/Bluetooth, or speaker output.

## Authoritative references

- Espressif, [ESP32-P4-Function-EV-Board v1.4 User Guide](https://docs.espressif.com/projects/esp-dev-kits/en/latest/esp32p4/esp32-p4-function-ev-board/user_guide_v1.4.html).
- Espressif, [ESP32-P4-Function-EV-Board documentation index](https://docs.espressif.com/projects/esp-dev-kits/en/latest/esp32p4/esp32-p4-function-ev-board/index.html).
