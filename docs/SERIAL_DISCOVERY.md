# Serial Port Discovery

## Primary selection

The application queries `Win32_PnPEntity` and extracts the current `COMx` value from enumerated device names. A port whose PnP device identifier contains Espressif VID `303A` and PID `1001` is selected first. This identifies the ESP32-P4 native USB Serial/JTAG function independently of the COM number assigned by Windows.

## Compatibility fallback

If the VID/PID query is unavailable, or no matching PnP entry is found, the application reads `SerialPort.GetPortNames()`. A single enumerated port may be selected as a compatibility fallback. If multiple nonmatching ports are present, the application remains disconnected instead of choosing an arbitrary device.

The scan repeats every three seconds while disconnected. A connected port failure closes the handle and retries after 2.5 seconds.

## Driver requirements

The validated transport does not contain a CH340 bridge and therefore does not require a CH340 driver. Windows 10 and Windows 11 normally bind their inbox USB serial support to the Espressif function. A driver for CH340, CP210x, FTDI, or another bridge is relevant only when that separate bridge is deliberately used.

PnP display strings may be localized; selection depends on VID/PID and the `COMx` suffix, not on the English device description.
