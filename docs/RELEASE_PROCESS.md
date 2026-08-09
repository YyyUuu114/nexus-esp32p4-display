# Release Publication Process

This repository is a publication surface, not the active development workspace. A release maintainer must construct each branch from an isolated staging directory and must not add a development workspace as the Git repository root.

## Required gates

1. Copy only reviewed source, reproducible configuration, documentation, and required assets into a new staging directory.
2. Exclude build trees, dependency caches, editor state, logs, screenshots containing host data, and unpublished design material.
3. Normalize version and protocol metadata across source, documentation, update manifests, and machine-readable release files.
4. Review every third-party dependency for an official source, pinned version or bounded range, integrity hash, and applicable license.
5. Scan the complete candidate tree for credentials, tokens, user-profile paths, host names, fixed serial ports, device identifiers, and oversized files.
6. Build from the staged source. Generate SHA-256 digests for every release asset.
7. Commit only the audited snapshot to the component branch. Attach binaries to a GitHub Release; do not commit them to branch history.
8. Verify the public branch list, default branch, release assets, checksums, and rendered documentation after push.

Stable publication requires a `.0` product version and completed host validation. Development publication requires a `.1` through `.9` product version and must be identified as pre-release material in both README and `release.json`.
