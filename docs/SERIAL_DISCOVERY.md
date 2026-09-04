# Serial Port Discovery and Device Negotiation

## Candidate selection

The application queries `Win32_PnPEntity`, extracts each `COMx` suffix, and retains only entries whose PnP identifier contains Espressif VID `303A` and PID `1001`. The last port that completed a handshake during the current process is tried first if it remains enumerated. COM numbers are never persisted or hard-coded.

If WMI itself is unavailable, the application may probe names returned by `SerialPort.GetPortNames()` so a restricted management provider does not make recovery impossible. When WMI succeeds but reports no matching Espressif function, generic serial devices are not opened. The former rule that trusted a system's only serial port has been removed.

## Mandatory identity check

Opening a port is not a successful connection. For each candidate, the application:

1. creates a cryptographically random 128-bit nonce;
2. sends the protocol-2.0 `hello` object;
3. waits no more than 1.5 seconds for a bounded UTF-8 JSON line;
4. rejects duplicate JSON properties, fractional protocol fields, product mismatch, protocol mismatch, unsupported product versions, or a nonce mismatch;
5. retains the handle only after a valid `ready` response.

Each telemetry frame includes the negotiated nonce. A manual reconnect closes the handle and requires a new nonce. Details are normative in [../PROTOCOL.md](../PROTOCOL.md).

## Retry and overhead

System sampling and serial transport use independent dedicated threads. Sampling continues once per second while the board is absent, preserving process-session energy semantics, and transport continues when a hardware provider is delayed. Candidate discovery is limited to once every three seconds. Serial reads use 200 ms sub-timeouts under an overall 1.5-second handshake deadline; no busy loop is used. If the Windows transmit queue remains non-empty for three consecutive one-second cycles, the handle is treated as stalled, closed, and negotiated again.

## Driver requirements

The validated runtime transport is ESP32-P4 native USB Serial/JTAG and does not contain a CH340 bridge. Windows 10 and Windows 11 normally bind their inbox support to the Espressif function. A CH340, CP210x, or FTDI driver is relevant only if that separate external bridge is deliberately used; such a bridge is not part of the validated protocol-2.0 assembly.
