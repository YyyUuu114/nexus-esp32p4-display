# Release Publication Process

This repository is a publication surface, not the active development workspace. A release maintainer must construct each branch from an isolated staging directory and must not add a development workspace as the Git repository root.

## Required gates

1. Copy only reviewed source, reproducible configuration, documentation, and required assets into a new staging directory.
2. Exclude build trees, dependency caches, editor state, logs, screenshots containing host data, and unpublished design material.
3. Normalize version and protocol metadata across source, documentation, update manifests, and machine-readable release files.
4. Review every third-party dependency for an official source, pinned version or bounded range, integrity hash, and applicable license.
5. Scan the complete candidate tree for credentials, tokens, user-profile paths, host names, fixed serial ports, device identifiers, and oversized files.
6. Build from the staged source with the repository-external ECDSA key. Decode and review the signed payload, verify that its URL, protocol, peer requirement, version range, and package SHA-256 match `release.json` and the exact ZIP.
7. Copy only the reviewed signed envelope to `package/latest-update.json`; never copy the private key, restored dependencies, or executable artifacts into source.
8. Commit and push the audited snapshot to the component development branch first. Only after the branch is public, create the declared GitHub prerelease and upload the exact ZIP, envelope, and checksum file; do not commit binaries to branch history.
9. Verify the branch, workflow result, prerelease flag, asset hashes, signed envelope fetched through the pinned raw URL, and rendered documentation.

Stable publication requires a `.0` product version and completed host validation. Development publication requires a `.1` through `.9` product version and must be identified as pre-release material in both README and `release.json`.
