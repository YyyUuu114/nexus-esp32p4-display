# Signed Update Format and Atomic Installation

## Trust root

Development v2.1.1 pins key `nexus-dev-2026-01`, an ECDSA P-256 SubjectPublicKeyInfo value recorded in `package/update-trust.json` and compiled into `BuildInfo.cs`. The private key is generated with `tools/new-update-signing-key.ps1`, stored outside the repository with a user-only ACL, and never copied into source or release assets.

The project signature protects the update manifest and package identity. It is independent of TLS and remains mandatory after HTTPS succeeds. It is not a substitute for commercial Authenticode reputation; `tools/build-release.ps1` accepts an optional certificate thumbprint when a maintainer has an appropriate code-signing certificate.

## Envelope

`package/latest-update.json` is a small JSON envelope:

```json
{
  "schemaVersion": 1,
  "keyId": "nexus-dev-2026-01",
  "payload": "BASE64_OF_EXACT_UTF8_JSON_BYTES",
  "signature": "BASE64_OF_DER_ECDSA_SIGNATURE"
}
```

The signature is ECDSA P-256 with SHA-256 over the decoded payload bytes and DER (`RFC 3279`) encoding. Duplicate JSON properties, unknown key identifiers, malformed Base64, an invalid signature, or oversized data are rejected before the payload is used.

The signed payload contains component, channel, `MAJOR.LINE.CHANNEL` version, official GitHub Release URL, package SHA-256, wire major/revision, update class, peer requirement, paired firmware, and the supported firmware range. Runtime code executes the update-class rules; for example, `single-endpoint` cannot change the wire protocol, while `coordinated-breaking` must require and identify a peer update.

## Origin restrictions

The installed `update-channel.json` must exactly match the compile-time development channel and fixed raw GitHub manifest URL. The signed package URL must be HTTPS and begin with this repository's GitHub Release path. Arbitrary user-configured domains and unsigned local ZIP selection are intentionally unsupported.

## Package validation

The helper copies the downloaded ZIP from `%LOCALAPPDATA%` into the protected install root, then rechecks the signed SHA-256 there. This closes the user-writable time-of-check/time-of-use window. Extraction rejects traversal, rooted paths, alternate data streams, duplicate destinations, invalid Windows path segments, reparse points/symbolic links, expanded data above 600 MiB, and packages above 300 MiB. The embedded executable file version must equal the signed product version.

## Atomic swap and rollback

The running application copies its trusted updater executable to `C:\Program Files\NEXUS Display\updater`. After the parent exits, the helper extracts to a unique `incoming-*` directory and performs:

1. remove the previous retained rollback;
2. rename `current` to `rollback`;
3. rename `incoming-*` to `current`;
4. start the new protected executable and observe it for 2.5 seconds;
5. if it exits or any operation fails, restore `rollback` to `current` and relaunch the previous version.

No generated PowerShell script or executable from a user-writable directory is launched. One rollback directory is retained for recovery until the next successful update.
