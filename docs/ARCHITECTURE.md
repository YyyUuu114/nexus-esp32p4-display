# Desktop Architecture

## Process and privilege model

The distribution is a self-contained WinForms tray executable with a global single-instance mutex. First launch from an extracted package copies the executable and fixed update configuration into `C:\Program Files\NEXUS Display\current`, then relaunches from that protected path. Administrator elevation supports protected installation and hardware sensors.

Optional login startup is a Windows Scheduled Task created only after explicit user action. Its action must resolve exactly to the protected current executable with `--background`; querying the task also verifies this command. Installation itself leaves startup disabled.

## Sampling and energy

One background worker owns one LibreHardwareMonitor `Computer`, one serial handle, and the enabled hardware graph. Hardware and network counters update once per second. The displayed primary GPU is chosen deterministically once instead of switching with instantaneous load.

Session energy integrates non-negative finite CPU power plus one preferred board/package-power sensor from every GPU. Invalid, negative, or combined readings above 20 kW are discarded. Gaps over ten seconds are excluded, and increments are constrained to be non-negative, so cumulative energy is monotonic for the process lifetime. Serial disconnection does not reset it.

## Transport

Candidate selection, handshake, and retry behavior are documented in [SERIAL_DISCOVERY.md](SERIAL_DISCOVERY.md). A port is connected only after protocol-2.0 mutual version and nonce validation. JSON telemetry is sent once per second with bounded values and a sanitized 15-character ASCII host label.

## Persistent state

No telemetry history is written. `%LOCALAPPDATA%\NexusDisplay` contains short logs and temporary downloaded update material. Logs rotate at 256 KiB, retain two historical files, and delete files older than seven days. Update staging older than two days is removed during later update checks.

Protected application state is limited to `current`, `updater`, and at most one `rollback` directory under Program Files. The application does not create startup state unless requested.

## Network behavior

Normal telemetry operation is offline. Network access occurs only when the user invokes update checking. The manifest origin is compile-time pinned; update identity, package hash, extraction, and rollback controls are described in [UPDATE_FORMAT.md](UPDATE_FORMAT.md).
