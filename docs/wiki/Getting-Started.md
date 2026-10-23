# Getting started with the implementation preview

The first implementation is an alpha preview. USB discovery/writes, status profiles,
raw programs, silent iPhone bridge delivery with an existing APNS token, session
aggregation and a foreground Copilot CLI host are implemented. QR pairing, protected
credential storage, background-service installation, full LED grammar validation,
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

## Phone delivery

Set `SIDEPULSE_TOKEN` in the current process environment to an existing iPhone push
token from the SidePulse app. Keep it out of scripts, command history, source
control and screenshots. The preview does not read or modify the Python port's
configuration, and does not persist tokens.

```powershell
sidepulse-net state working --token-env SIDEPULSE_TOKEN
sidepulse-net host --token-env SIDEPULSE_TOKEN
```

The bridge uses HTTPS and silent APNS payloads with unique event IDs. An HTTP
success is bridge acceptance, not proof of delivery to the hardware. Android,
custom bridge servers, phone registration and QR pairing are not implemented yet.

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
