# Update Channel and Manifest

The file next to `Nexus Display.exe` selects exactly one update channel:

```json
{
  "channel": "stable",
  "manifestUrl": "https://raw.githubusercontent.com/example/repository/desktop-stable/update/latest.json"
}
```

The HTTPS manifest schema is:

```json
{
  "component": "desktop",
  "channel": "stable",
  "version": "1.2.0",
  "downloadUrl": "https://github.com/example/repository/releases/download/v1.2.0/NEXUS-Display-Windows-x64.zip",
  "sha256": "64_HEXADECIMAL_CHARACTERS",
  "protocolMajor": 1,
  "updateClass": "single-endpoint",
  "peerUpdateRequired": false,
  "pairedFirmwareVersion": null
}
```

Stable manifests accept only product versions ending in `.0`; development manifests accept only `.1` through `.9`. A manifest with a different component or channel is rejected. If `peerUpdateRequired` is true, `pairedFirmwareVersion` is mandatory and the application shows an explicit coordinated-update warning before download.
