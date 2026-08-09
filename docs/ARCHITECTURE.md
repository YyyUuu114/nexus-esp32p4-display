# Desktop Architecture

## Process model

NEXUS Display is a single WinForms tray process. A global named mutex prevents duplicate instances. The telemetry worker owns one LibreHardwareMonitor `Computer` object, one background task, and at most one serial port handle.

## Sampling and transport

Each iteration updates the enabled CPU, GPU, motherboard, controller, and power-monitor hardware nodes, selects preferred sensors by stable name patterns, reads operating-system memory and network counters, serializes one JSON frame, and waits one second. Missing or invalid sensor values remain JSON `null`.

Network throughput is derived from cumulative interface byte counters. Session energy uses trapezoidal integration of the available CPU and GPU power values. Elapsed gaps greater than ten seconds are excluded because suspend and hibernate intervals cannot be represented by the sensor endpoints.

USB discovery and reconnect run in the same worker. Disconnected scans occur every three seconds; connection failures use a 2.5-second retry delay. No busy loop is used.

## Persistent state

The application stores no telemetry history. The only persistent application data is short diagnostic logging under `%LOCALAPPDATA%\NexusDisplay`, temporary update staging, and an optional Windows Scheduled Task named `Nexus Display Hardware Monitor`.

The scheduled task is created only after explicit user action and can be removed by the tray menu, command-line maintenance option, or packaged cancellation script.

## Network behavior

Normal telemetry operation does not require Internet access. Network access occurs only when the user invokes update checking and the packaged HTTPS manifest URL is configured. Online packages are size-limited and must match the manifest SHA-256.
