# Getting started with the implementation preview

The first implementation is an alpha preview. USB discovery/writes, status profiles,
raw programs, silent iPhone bridge delivery with an existing APNS token, session
aggregation, QR pairing, Windows-protected phone links, diagnostics and a foreground
Copilot CLI host are implemented. Background-service installation, full LED grammar validation,
animation rendering, migration and other agent integrations remain planned.

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

Run `artifacts/win-x64/sidepulse-net.exe`. The CI Windows job also publishes a
self-contained folder as an Actions artifact. This is a preview build, not a signed
installer or stable release.

## USB device

```powershell
sidepulse-net devices --json
sidepulse-net state working --device E:\
sidepulse-net write 'off\n#ff9900 1.6s pulse\nrepeat' --escapes --device E:\
```

An explicit directory must contain `LEDS.LED`; the tool will not create device files
on unrelated volumes. With no target, exactly one discovered device is selected.
Only one writer should control a device. Physical device behavior still needs
Windows hardware verification. Validation checks the 512-byte/20-line envelope;
the firmware remains responsible for LED syntax validation.

## Pair an iPhone on Windows

```powershell
sidepulse-net link pair iphone
sidepulse-net link list
sidepulse-net state done --phone iphone
sidepulse-net host --phone default
```

Scan the terminal QR using the SidePulse app's pairing flow. The pairing URI is
also shown for terminals without ANSI support or enough width. Pairing expires
after five minutes, supports Ctrl+C cancellation and ignores unrelated or invalid
registration events. `--timeout 60` shortens the wait; `--no-qr` shows only the URI.
Treat the live pairing URI as temporary pairing access; keep it out of shared logs.
Actual QR scanning with the TestFlight app still requires device verification.

The entire link document, including names and tokens, is encrypted with Windows
DPAPI for the current user. It is stored in `%LOCALAPPDATA%\SidePulse.NET\links.dpapi`.
There is no plaintext fallback. The store uses exclusive locking and atomic encrypted
file replacement. It preserves an unreadable store rather than overwriting it.
`SIDEPULSE_NET_HOME` can select another absolute directory, for isolated configurations
or testing; this does not make DPAPI credentials portable to another Windows user.
The app does not read or modify the Python port's configuration.

The first linked phone becomes the default. Selection is explicit via `--phone`;
commands without a phone option continue to discover USB devices.

```powershell
sidepulse-net link rename iphone personal
sidepulse-net link default personal
sidepulse-net link list --json
sidepulse-net link remove personal
sidepulse-net doctor --json
```

Aliases are case-insensitive and use 1–40 letters, digits, underscores or hyphens.
`default` is reserved. Existing aliases and duplicate tokens are rejected.
Removing a default link clears the default; it never silently selects another phone.
Removing a link only removes the local credential, and does not revoke the token
at the phone or bridge. Listing links and diagnostics never display tokens.
`doctor` checks local runtime, mounted devices, DPAPI/storage and hook-directory
presence; it sends no device writes or network requests.

## Existing tokens and other platforms

If you already have an iPhone token, set `SIDEPULSE_TOKEN` in the current process
environment. Keep the value out of command history, source control and screenshots.
Save it securely on Windows, or use it directly without persistence:

```powershell
sidepulse-net link import iphone --token-env SIDEPULSE_TOKEN
sidepulse-net state working --token-env SIDEPULSE_TOKEN
sidepulse-net host --token-env SIDEPULSE_TOKEN
```

Saved phone links and pairing currently require Windows. Linux/macOS support
environment-token delivery without persistent storage. `--device`, `--phone` and
`--token-env` are mutually exclusive. A dry run previews the program without
reading credentials or validating that a saved phone exists.

The bridge uses HTTPS and silent APNS payloads with unique event IDs. An HTTP
success is bridge acceptance, not proof of delivery to the hardware. Android and
custom bridge servers are not implemented yet. Pairing and delivery follow the
upstream protocol at the revision in `upstream.json`.

## Copilot CLI on Windows

Start the host in a terminal, choosing either USB or the phone environment token:

```powershell
sidepulse-net host --device E:\
```

Create the hook directory if needed, then generate a new, separate hook file:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.copilot\hooks" | Out-Null
sidepulse-net hook-config --exe C:\Tools\SidePulse.NET\sidepulse-net.exe --output "$env:USERPROFILE\.copilot\hooks\sidepulse-net.json"
```

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
