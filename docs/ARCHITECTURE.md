# Desktop Architecture

## Process and privilege model

The distribution is a self-contained WinForms tray executable with a global single-instance mutex. First launch from an extracted package copies the executable and fixed update configuration into `C:\Program Files\NEXUS Display\current`, then relaunches from that protected path. Administrator elevation supports protected installation and hardware sensors.

Optional login startup is a Windows Scheduled Task created only after explicit user action. Its action must resolve exactly to the protected current executable with `--background`; querying the task also verifies this command. Installation itself leaves startup disabled.

## Sampling and energy

One LibreHardwareMonitor `Computer` owns the enabled hardware graph. System sampling and serial transport run on separate dedicated background threads at one-second intervals; serial transport uses above-normal thread priority so CPU saturation or managed thread-pool pressure cannot suppress the heartbeat. GPU updates run as one isolated asynchronous task and are never overlapped. A blocked GPU provider therefore cannot stop CPU, memory, network, or serial work. GPU readings older than five seconds are replaced with missing values until the provider recovers.

The displayed primary GPU is chosen deterministically once instead of switching with instantaneous load.

Session energy integrates non-negative finite CPU power plus one preferred board/package-power sensor from every GPU. Invalid, negative, or combined readings above 20 kW are discarded. Gaps over ten seconds are excluded, and increments are constrained to be non-negative, so cumulative energy is monotonic for the process lifetime. Serial disconnection does not reset it.

## Transport

Candidate selection, handshake, and retry behavior are documented in [SERIAL_DISCOVERY.md](SERIAL_DISCOVERY.md). A port is connected only after protocol-2.0 mutual version and nonce validation. The transport thread sends the latest complete immutable snapshot once per second with bounded values and a sanitized 15-character ASCII host label. A transmit queue that remains non-empty for three cycles is treated as a stalled link and reopened. When acquisition is delayed, repeated frames retain fresh sequence and wall-clock fields so firmware link supervision remains valid; the tray and throttled log expose the sample delay.

## Persistent state

No telemetry history is written. `%LOCALAPPDATA%\NexusDisplay` contains short logs and temporary downloaded update material. Logs rotate at 256 KiB, retain two historical files, and delete files older than seven days. Update staging older than two days is removed during later update checks.

Protected application state is limited to `current`, `updater`, and at most one `rollback` directory under Program Files. The application does not create startup state unless requested.

## Network behavior

Normal telemetry operation is offline. Network access occurs only when the user invokes update checking. The manifest origin is compile-time pinned; update identity, package hash, extraction, and rollback controls are described in [UPDATE_FORMAT.md](UPDATE_FORMAT.md).
