# Host management

One shared host collects supported agent events and owns the selected output.
Start it before starting your agents. It uses current-user IPC, not a network
listener. The phone token remains in the native credential store or process
environment; it is never added to command arguments or logs.

```shell
sidepulse-net host start --phone default
sidepulse-net host status
sidepulse-net host status --json
sidepulse-net host logs --tail 30
sidepulse-net host stop
```

Choose a target with `--phone`, `--device` or `--token-env`. `--dry-run` runs the
host without delivery. `host start` launches a detached process and waits for an
IPC readiness response. The executable directory is its working directory;
explicit USB paths are made absolute before starting. Use `--exe` when invoking
through `dotnet` rather than a published apphost. Starting again reports the
existing host and does not silently switch its target.

`host status` returns running state, PID, aggregate state and provider-specific
session snapshots. `host stop` requests shutdown over the same current-user IPC;
it never blindly kills a PID from a stale file. The host leaves the last LED
program in place. Stopping does not mark unfinished work complete. Sessions are
in memory only and begin unknown after restart.

For a foreground session:

```shell
sidepulse-net host run --phone default
```

The earlier `sidepulse-net host --phone default` shorthand remains supported.
Ctrl+C stops the foreground host. Upgrade by stopping the old host before
replacing binaries; older preview hosts do not implement the control protocol.
A process started from a terminal is detached on Unix. Windows launches it without
a console window. Background logs live under `host/host.log` in the SidePulse.NET
configuration directory, with rotation around 1 MiB and one previous file. Logs
contain state/count diagnostics, not prompts, tool arguments or credentials.
`host logs` reads the current log; `.previous` remains available for inspection.

Login startup registration and system service installation are not implemented
in this preview. `host start` starts a user process for the current login session.
Linux desktop/Keychain availability can change after logout or keyring locking;
check `doctor` and the host log if phone output becomes unavailable.

Tests exercise detached start, idempotent start, status snapshots, mixed-provider
hook delivery and graceful stop. Foreground tests also reject competing hosts
and verify restart after forced termination. These are simulated device tests,
not evidence of actual LED or phone receipt.

## Clearing abandoned sessions

`sidepulse-net host reset` clears all tracked sessions and queues the Unknown
pattern. Use `--json` for a machine-readable snapshot. This explicitly discards
history; it does not mark tasks successful or stop agent processes. The next agent
event establishes fresh state. A stopped host returns exit code 1.

IPC messages and host snapshots now carry `ProtocolVersion: 1`. Unknown explicit
versions are rejected; legacy messages without the field are treated as v1.
This does not establish compatibility between arbitrary application versions:
stop the host before upgrades and run matching CLI/host binaries. Restart remains
Unknown with no restored session history; reset provides explicit recovery from
abandoned sessions without a timeout inventing successful completion.
