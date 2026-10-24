# Platform support

The preview supports Windows, macOS and Linux. The core, terminal UI, QR/SSE
pairing, phone delivery, local host, hook generation and mounted USB file transport
share one codebase. Native credentials are selected at runtime.

| Platform | Saved phone protection | Requirements | Archives |
|---|---|---|---|
| Windows | Current-user DPAPI; existing store format retained | Windows desktop user profile | x64, ARM64 ZIP |
| macOS | Native SecItem Keychain API holds an AES-256-GCM key | Unlocked Keychain and access for the executable | x64, ARM64 tar.gz |
| Linux | Secret Service holds an AES-256-GCM key | `secret-tool`, unlocked keyring, D-Bus session; glibc distribution | x64, ARM64 tar.gz |

Linux uses the fixed `/usr/bin/secret-tool` or `/bin/secret-tool` helper without a
shell. Encryption keys pass through stdin/stdout, never command arguments. The
helper has a bounded timeout. macOS calls Security/CoreFoundation directly and
fails with a useful error if Keychain access cannot proceed without a prompt.
Unlock the login Keychain and grant application access before starting the host.
There is no plaintext key file or fallback to unprotected persistence.

A key is stored per absolute configuration path. Removing a phone removes the
local link, not the native encryption key or the remote registration. A missing
vault key does not cause replacement of an existing encrypted store. New Unix
store and lock files have owner-only permissions. Existing Windows DPAPI stores
continue to use `links.dpapi`; Unix stores use `links.encrypted`.

The CLI's `doctor` command reports backend availability and checks an existing
store. It does not create an encryption key or prove that an absent store can be
created in a locked vault. `--token-env` remains available on headless machines
without a credential service. No root/admin access is required by the application;
the user must have write permission for the mounted device and config directory.

## Tests and distribution

CI runs builds, behavioral tests and simulated USB/host/CLI checks on Windows,
Linux and macOS. Linux CI provisions an isolated D-Bus/keyring session; macOS CI
creates and deletes a temporary Keychain. Native encryption and phone management
are exercised on all three. Bash hook commands are executed on Unix runners. Host tests reject competing
instances and verify restart after forced termination. Unix IPC uses a short
per-user private directory, an exclusive host lease and current-user-only sockets.

Each platform produces x64 and ARM64 archives. CI executes the archive matching
its runner architecture; the other architecture is cross-published. This does
not establish hardware verification for every architecture. Alpine/musl,
32-bit systems, macOS notarization, signed Windows installers and OS service
installation are not included in this preview.

USB discovery uses mounted drives and the existing `LEDS.LED` file. Explicit
`--device` paths work when autodiscovery cannot find a mount. On macOS use a
mounted path such as `/Volumes/SidePulseDot`; on Linux use its actual mount path,
for example `/media/$USER/SidePulseDot`. Do not run two writers for one device.
Actual hardware, TestFlight scans and real Copilot installations still require
manual verification on each target OS.
