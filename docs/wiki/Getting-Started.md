> Shared background host commands and managed setup for Codex, Claude Code, Kiro
> and Copilot are now implemented. See [[Host-Management]] and [[Agent-Integrations]].
> The manual Copilot hook generation below remains available for existing users.

# Getting started with the beta candidate

The executable is `spn` (`spn.exe` on Windows). Run `.\spn.exe` in PowerShell or
`./spn` on Unix, then optionally `install --dry-run` and `install --add-to-path`.
Single-file builds contain the runtime, docs and licenses; keep the small manifest
for portable self-updates, or let install create it. See [[Installation]].

USB/SD writes, iPhone pairing/protected links, shared agent host, animation studio,
managed integrations and a persistent interactive session are implemented.
Per-user installation and optional PATH setup are available. Saved UI preferences,
remaining upstream integrations and physical firmware acceptance remain open.

## Build and run

Install the .NET 10 SDK, then:

```shell
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/SidePulse.Cli -- dashboard
dotnet run --project src/SidePulse.Cli -- state working --dry-run --json
```

Publish a Windows application without a Python or .NET runtime requirement:

```shell
dotnet publish src/SidePulse.Cli -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/spn.exe`. For macOS or Linux, replace `win-x64`
with the desired runtime identifier and run `./spn`. CI publishes
archives for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` and
`linux-arm64`. Windows archives use ZIP; Unix archives use tar.gz to retain
executable permissions. Extract the inner archive after downloading the Actions
artifact. See [[Platforms]] for prerequisites and validation limits.
These are unsigned preview builds, not installers or stable releases.

## USB device

```powershell
spn devices --json
spn state working --device E:\
spn write 'off\n#ff9900 1.6s pulse\nrepeat' --escapes --device E:\
```

An explicit directory must contain `LEDS.LED`; the tool will not create device files
on unrelated volumes. With no target, exactly one discovered device is selected.
Only one writer should control a device. Physical device behavior still needs
Windows hardware verification. Validation checks the 512-byte/20-line envelope;
the firmware remains responsible for LED syntax validation.

## Pair an iPhone

```powershell
spn link pair iphone
spn link list
spn state done --phone iphone
spn host --phone default
```

Scan the terminal QR using the SidePulse app's pairing flow. The pairing URI is
also shown for terminals without ANSI support or enough width. Pairing expires
after five minutes, supports Ctrl+C cancellation and ignores unrelated or invalid
registration events. `--timeout 60` shortens the wait; `--no-qr` shows only the URI.
Treat the live pairing URI as temporary pairing access; keep it out of shared logs.
Actual QR scanning with the TestFlight app still requires device verification.

The entire link document, including names and tokens, is protected by the native
credential system. Windows keeps the existing DPAPI format at
`%LOCALAPPDATA%\SidePulse.NET\links.dpapi`. macOS and Linux encrypt a
`links.encrypted` file using AES-256-GCM, with a random key held in Keychain or
Secret Service respectively. The platform's local-application-data directory is
used unless `SIDEPULSE_NET_HOME` selects another absolute directory.

There is no plaintext fallback. The store uses exclusive locking and atomic
encrypted replacement, with owner-only permissions for new Unix files. Missing
keys and unreadable stores are preserved, not reset. Vault keys are scoped to the
absolute store path; moving a Unix store requires pairing again or restoring its
original location. Copying the encrypted file alone is not a credential migration.
The app does not read or modify the Python port's configuration.

macOS needs an unlocked Keychain and access for the executable. Linux needs
`secret-tool`, an unlocked Secret Service keyring and a working D-Bus session.
On Debian/Ubuntu, install `libsecret-tools`; desktop sessions commonly provide the
keyring service. Headless Linux needs its own unlocked Secret Service session, or
can use environment-token delivery without saved links. See [[Platforms]].

The first linked phone becomes the default. Without target options, state, write,
animation apply and host delivery use exactly one mounted USB device; if none is
found, they use the saved default phone (or the only saved phone). For example,
`spn state working` works with a paired default phone and no USB device.
Multiple mounted devices require an explicit target. Multiple phones without a
default require `--phone` or `link default <alias>`. Explicit `--device`, `--phone`
and `--token-env` always take precedence. A failed selection, credential read or
delivery never retries on a different output.

```powershell
spn link rename iphone personal
spn link default personal
spn link list --json
spn link remove personal
spn doctor --json
```

Aliases are case-insensitive and use 1–40 letters, digits, underscores or hyphens.
`default` is reserved. Existing aliases and duplicate tokens are rejected.
Removing a default link clears the saved default. Automatic delivery can use the
only remaining phone; with multiple remaining phones, choose a new default.
Removing a link only removes the local credential, and does not revoke the token
at the phone or bridge. Listing links and diagnostics never display tokens.
`doctor` checks local runtime, mounted devices, DPAPI/storage and hook-directory
presence; it sends no device writes or network requests.

## Existing tokens and other platforms

If you already have an iPhone token, set `SIDEPULSE_TOKEN` in the current process
environment. Keep the value out of command history, source control and screenshots.
Save it securely using the native credential vault, or use it directly without persistence:

```powershell
spn link import iphone --token-env SIDEPULSE_TOKEN
spn state working --token-env SIDEPULSE_TOKEN
spn host --token-env SIDEPULSE_TOKEN
```

Saved links and pairing are available on Windows, macOS and Linux when the native
credential backend is available. Environment-token delivery needs no persistent
credential backend. `--device`, `--phone` and
`--token-env` are mutually exclusive. A dry run previews the program without
reading credentials or validating that a saved phone exists.

The bridge uses HTTPS and silent APNS payloads with unique event IDs. An HTTP
success is bridge acceptance, not proof of delivery to the hardware. Android and
custom bridge servers are not implemented yet. Pairing and delivery follow the
upstream protocol at the revision in `upstream.json`.

## Copilot CLI

Start the host in a terminal, choosing either USB or the phone environment token:

```powershell
spn host --device E:\
```

Create the hook directory if needed, then generate a new, separate hook file:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.copilot\hooks" | Out-Null
spn hook-config --exe C:\Tools\SidePulse.NET\spn.exe --output "$env:USERPROFILE\.copilot\hooks\sidepulse-net.json"
```

On macOS/Linux, use:

```bash
mkdir -p ~/.copilot/hooks
spn hook-config --exe /absolute/path/to/spn --output ~/.copilot/hooks/sidepulse-net.json
spn host --phone default
```

Generated files include both Bash and PowerShell wrappers. Bash inherits JSON
stdin and always returns zero, including when the executable is unavailable.
If `COPILOT_HOME` is set, use its `hooks` directory instead. Existing files are never
overwritten. Restart Copilot CLI after installation. PowerShell 7 is required for
Copilot's Windows shell hooks. Remove this file and restart Copilot to uninstall.
Avoid running the Python and C# hook integrations for the same output simultaneously.

Hooks forward stdin to a current-user-only named pipe. They have a short connection
budget, emit no output and always return zero for runtime input/connection failures.
The generated PowerShell wrapper also returns zero if the executable is unavailable.
No prompt, tool arguments or transcript content is forwarded to the host, persisted
or sent to the bridge. The host accepts bounded messages and sessions; network
output runs separately through a latest-state queue. Rapid intermediate states may
be coalesced. If the host is absent or overloaded, hook events may be dropped.
Start the host before Copilot. Restarted hosts start unknown and do not replay old
sessions. Crashed/missing sessions are not inferred complete; restart the host to
clear stale state. Stopping the host leaves the last LED program on the device.

Waiting takes precedence over working, then errors, completed turns and idle.
Completion is inferred only from `agentStop` with `stopReason=end_turn`, never a
subagent/background completion or timeout. The host UI appends session tables on
events; `--plain` provides simple lines and `--json` provides structured snapshots.
The standalone `dashboard` is a static overview in this preview.

The event mapping follows the [GitHub Copilot hook reference](https://docs.github.com/en/copilot/reference/hooks-reference).
Actual Copilot/Windows and phone/hardware validation remain separate from unit
and simulated transport tests. Keep the working Python installation available
until this preview is validated on your setup.

## Animation studio

See [[Animations]] for Dot/Pro previews, custom editing and agent profiles, and
[[Scripting-and-Menus]] for interactive command building and quiet/JSON output.

## Updates

See [[Updating]] for GitHub release checks, verified self-update and recovery.
