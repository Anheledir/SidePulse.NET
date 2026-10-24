# Installation and portable use

`spn` is a self-contained single executable (`spn.exe` on Windows). Copying that
executable alone is enough for portable use, offline documentation and licenses.
The release archive also contains `sidepulse-install.json`, a small updater
manifest. Native .NET libraries can be extracted into the runtime's per-user
bundle cache; single-file does not mean zero files are ever written.

## Install for this user

```sh
./spn install --dry-run
./spn install
./spn install --status --json
```

Use `.\spn.exe` in PowerShell. Installation is also available from **Settings →
Installation** in the interactive session, with a preview and explicit Install action.
Portable use remains available without running install or changing PATH.

Default binary directories:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\Programs\SidePulse.NET\app` |
| Linux | `~/.local/share/SidePulse.NET/app` |
| macOS | the current user's local application-data directory, under `SidePulse.NET/app` |

`--directory <absolute-path>` selects another dedicated directory. The default
requires no administrator privileges. Existing unowned directories, modified
application files and downgrades are refused. Repeat installation is idempotent.
An upgrade of a verified owned directory uses a backup, startup probe and rollback.
Source-directory files outside the package manifest are never copied.

## Optional user PATH

```powershell
.\spn.exe install --add-to-path --dry-run
.\spn.exe install --add-to-path
```

Windows changes **only the user PATH**, preserving existing entries and registry
value type. It does not copy machine PATH into user PATH. Existing command-name
conflicts are reported before installation. Restart terminals to pick up the
changed environment; the installed absolute path works immediately.

On Unix, a managed `~/.local/bin/spn` symlink points at the permanent executable.
If that directory is already on PATH, no shell profile changes are needed.
Otherwise choose the shell explicitly or let `SHELL` identify it:

```sh
./spn install --add-to-path --shell bash --dry-run
./spn install --add-to-path --shell bash
```

Supported profiles are `.bashrc` for Bash, `.zshrc` for Zsh, and `.profile` for sh/dash.
The preview shows the exact file and export block. An original profile gets a
byte-exact private backup before one marked block is appended. Profile permissions,
unrelated entries and subsequent edits outside that block are retained. Unknown
shells require an explicit choice; no profile is guessed or edited silently.
Open a fresh interactive shell afterward. Login-shell configurations that do not
source these profiles can use the absolute executable or their own PATH setup.

## Agents, host and existing settings

Stop the shared host before installing/upgrading. Configurations, phone links,
protected credentials, profiles and IPC identities keep their existing locations.
After installation, new agent integration and detached-host setup use the stable
installed absolute executable path, even when invoked from a portable copy.

Existing managed hooks are not overwritten automatically. Preview their removal
with the old executable, uninstall them using any original `--config` overrides,
then reinstall using the installed `spn`. Preserve modified files for manual review.
Restart agents and review their normal trust prompts. Deleting the original
download directory does not affect newly registered installed hooks.

## Uninstall and recovery

```sh
spn uninstall --dry-run
spn uninstall
```

Remove managed agent integrations first and stop the host. Uninstall checks
ownership/hashes, removes owned application and PATH/launcher entries, and preserves
settings, credentials, user-added files and profile backups. A modified launcher
or PATH block is refused rather than overwritten. No user data deletion option is
implied by uninstalling the executable.

When invoked from the installed application, a copied helper waits for the
calling process to exit. Confirmed self-removal closes the interactive session.
The command reports `scheduled`, not completed; the reported helper workspace
contains `result.json` after completion and remains available for recovery.
Removing from another portable copy completes directly. Locked executables or
permission errors require closing the relevant process/reviewing the reported
state; no process is forcibly killed. Settings remain available for reinstall.

Installation registration and PATH ownership are stored in `installation.json`
beside application settings, separate from protected phone credentials. `install
--status` and `doctor` show installation context without exposing credentials.
