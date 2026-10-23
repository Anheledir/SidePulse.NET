# Project setup and automation

Repository: [Anheledir/SidePulse.NET](https://github.com/Anheledir/SidePulse.NET).
The repository and wiki are public and use the MIT license.

## Workflows

| Workflow | Behavior |
|---|---|
| Quality | Validate documentation links, JSON, formatting and publisher tests |
| .NET CI | Restore, build and test a root solution on Windows, Linux and macOS |
| Wiki sync | Publish reviewed docs/wiki pages on the default branch |
| Upstream watch | Check upstream every Monday at 07:17 UTC and on manual dispatch |
| Security | CodeQL for C#, Python and Actions; PR dependency review blocks new high/critical advisories |
| Dependabot | Propose weekly dependency updates |

Actions are pinned to upstream commit SHAs. Permissions are read-only by default.
Wiki sync requests `contents: write` for its publication job and uses the ephemeral
`github.token`. No personal access token needs to be created or stored. The wiki
must already have an initial page; this repository's wiki has been initialized.
Wiki publication runs only on the default branch, never from a pull request.

The publisher updates only pages listed in its ownership manifest, preserves
unmanaged pages and removes formerly managed pages when removed from source.
Documentation changes belong in `docs/wiki`, rather than editing published pages.
A failed publication is visible as a failed workflow run.

## Upstream reviews

`upstream.json` records the last reviewed commit of `inteliwear/sidepulse`.
The scheduled workflow compares it with the upstream default branch. When changes
exist, it creates or updates one open tracking issue with the compare link and
changed files. It does not import or execute upstream code. Review changes, port
applicable behavior with tests and preserve licensing before advancing the
baseline in a pull request. Closing an issue without advancing the baseline will
cause the next check to create a new review issue.

The workflow uses the built-in Actions token with `issues: write`. No extra token,
external service or personal reminder is required. GitHub schedules may be delayed;
use **Actions → Upstream watch → Run workflow** for an immediate check. Public
repositories with no activity may have scheduled workflows disabled by GitHub.

## Security and repository administration

[SECURITY.md](https://github.com/Anheledir/SidePulse.NET/blob/implementation/first-preview/SECURITY.md)
explains private reporting and trust boundaries. The
[configuration record](https://github.com/Anheledir/SidePulse.NET/blob/implementation/first-preview/docs/repository-settings.md)
separates observed settings, source configuration and desired admin changes.
Adding a workflow does not prove that repository-level security controls are on.
