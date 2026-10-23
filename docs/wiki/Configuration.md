> Implementation update: Links use native credentials: Windows DPAPI, macOS Keychain or Linux Secret Service.
> Alias/default management and the `SIDEPULSE_NET_HOME` override are implemented.
> See [[Getting-Started]] for working commands. The remainder describes the target configuration design.

# Configuration and coexistence

Status: proposed.

Use separate current-user directories for configuration, runtime state and logs.
The Windows profile lives under a SidePulse.NET-specific location, distinct from
the existing Python port. Configuration has a schema version and documented defaults.

## Settings

Device aliases, preferred targets, per-state animations/brightness, active profile,
bridge origin, integration enablement and startup preference are configurable.
Secrets are stored separately from ordinary settings and never emitted by `--json`,
diagnostics, logs or terminal previews.

## Migration

Discover existing Python settings, show what can be imported, explain credentials
and unsupported fields, back up the source and require explicit selection before
writing. Never modify the working Python installation or its hook files silently.
Provide a migration report and a way to undo new configuration.

## Commands and conflicts

Initially expose `sidepulse-net`. An optional `sidepulse` alias is a deliberate
choice because the user already has a working Python command by that name.
Detect competing hooks and writers in diagnostics, and offer targeted repair
rather than automatically deleting another application's configuration.

## Observability

Use bounded, redacted logs and a clear export preview. Track state changes, output
attempts and failures without retaining conversation content. Distinguish idle,
unknown, stale and unreachable. Retention duration and completion display timeout
remain concept-review decisions.
