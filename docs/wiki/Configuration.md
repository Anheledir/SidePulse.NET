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

The executable is `spn` (`spn.exe` on Windows). No compatibility command or alias
is distributed. The Python `sidepulse` installation remains independent.
Detect competing hooks and writers in diagnostics, and offer targeted repair
rather than automatically deleting another application's configuration.

## Observability

Use bounded, redacted logs and a clear export preview. Track state changes, output
attempts and failures without retaining conversation content. Distinguish idle,
unknown, stale and unreachable. Retention duration and completion display timeout
remain concept-review decisions.

## Implemented application defaults

`spn settings show` displays the effective saved defaults and the settings path.
Use **Settings → Preferences** in the interactive shell, or:

```powershell
spn settings set --phone iphone --brightness 60 --shell powershell
spn settings set --profile personal --model dot
spn settings set --auto --no-profile
spn settings set --brightness 80 --dry-run
spn settings show --json
spn settings reset
```

Replace `iphone` with a saved alias; `default` follows the designated default phone.
Use `--device <absolute-path>` for a preferred USB/SD output. Explicit command
output flags override the saved output. Otherwise the saved output wins over USB
discovery. A missing or failing preferred output produces an error; it never
redirects delivery to another device. `--auto` restores USB/default-phone discovery.

Defaults are Auto output, 100% brightness, built-in state programs and the platform
export shell (PowerShell on Windows, Bash elsewhere). State sends and the host
use the selected profile/model and brightness; animation preview/apply use the
saved brightness/model unless explicitly overridden. Global brightness scales a
profile's own brightness. `state` and `host run/start` accept `--brightness`,
`--profile`, `--no-profile` and `--model` overrides. A preferred profile requires
an explicit model, so phone delivery does not need USB model detection. Restart
the host after changing its defaults.

The versioned `settings.json` lives next to the protected phone store, under
`SIDEPULSE_NET_HOME` when specified. It contains aliases/paths and ordinary
preferences, never push tokens or command history. Reading defaults and previewing
settings do not create files or open the credential vault. Invalid or newer-schema
settings are preserved with a recovery message. `settings reset` explicitly replaces
settings with defaults; phone links, profiles and integration configurations remain.
