# Checking and installing updates

```sh
sidepulse-net update
sidepulse-net update check --json
sidepulse-net update check --prerelease
sidepulse-net update install --prerelease --dry-run
sidepulse-net host stop
sidepulse-net update install --prerelease
sidepulse-net update status --json
sidepulse-net host start --phone default
```

A bare `update` checks only in scripts; in a terminal it offers a cursor menu.
Checks use GitHub public releases without an account/token. Stable releases are
the default. `--prerelease` includes published prereleases and selects the highest
semantic version. Drafts, tags without releases and Actions artifacts are excluded.
A prerelease lookup is bounded to 1,000 releases; rate limits fail with a concise
error. Equal/older versions never downgrade an installation. Missing release assets
or checksums prevent installation. No published release is a normal check result.

Installation is explicit. The updater chooses the current process architecture
(x64/ARM64), downloads the matching ZIP/tar.gz and `SHA256SUMS`, verifies SHA-256
before extraction and additionally checks GitHub asset digests when supplied.
It verifies a version/runtime/file-hash manifest and rejects archive traversal,
links, duplicate files, special entries and excessive sizes. Checksums establish
integrity against the published release metadata; they are not independent code
signatures or protection against a compromised publisher account.

## Replacement and recovery

Self-update requires a self-contained distribution containing `sidepulse-install.json`.
Source builds, framework-dependent binaries, older previews without that manifest,
modified application files and symlinked installations require manual installation.
The first updater-capable preview is 0.1.0-alpha.6. Older versions must be upgraded
manually once. The install directory and its parent must be writable; the updater
never elevates privileges. Package-manager installations should use that manager.

Run the command from outside the installation directory. Stop the agent host and
other processes using the installation first. The updater
does not kill processes or change startup registration. Downloading/staging leaves
the current installation intact. A helper copied outside the installation waits
for the calling process to exit, replaces the whole application directory, and
runs the new executable's version check. A failed replacement/probe attempts to
restore the original directory. A per-installation lock prevents concurrent swaps.

`update install` returning success means **scheduled**, not already installed.
Run `update status` after it exits; wait for `installed` before restarting the host
with its previous target/profile options. Failure/recovery states return exit 1.
The JSON receipt includes target, backup, workspace and state. If interrupted,
status may remain scheduled/applying. Inspect those paths before manual recovery;
never delete the only intact backup. Updating is not a crash-atomic transaction
across the two directory renames. A power failure between them may require restoring
the backup directory manually before the CLI can run again.

Unknown user-added files are copied into the new directory unless they collide
with a new application-owned file. Avoid editing the installation during updating.
Phone links, profiles and hooks normally live outside it and are not changed.
The complete old installation and temporary download/helper workspace are retained
for recovery. Once installation is confirmed and no helper is running, these
paths may be removed manually to reclaim space. Failed updates preserve evidence.

## Publishing

The Release workflow runs on `vVERSION` tags or manual dispatch on such a tag.
The tag must match the project version. It reuses all three native CI jobs and
self-contained smoke checks, collects all six archives, writes `SHA256SUMS`, uploads
a draft release and only then publishes it. Hyphenated versions are prereleases.
No release or tag is created simply by adding this workflow to a branch. Existing
release tags are not overwritten. Review the release notes/assets after publishing.
