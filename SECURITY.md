# Security policy

SidePulse.NET is an unofficial, independently maintained MIT project. The current
implementation is an unreleased alpha in PR #12. No stable release or response-time
SLA is offered. Security fixes target the latest preview branch and subsequently
the latest published preview; older previews are not maintained independently.

## Report a vulnerability

Use [GitHub private vulnerability reporting](https://github.com/Anheledir/SidePulse.NET/security/advisories/new)
(enabled and verified 2026-10-24). If that page is unavailable, open an issue requesting a private
reporting channel without publishing exploit details, phone tokens, credentials,
logs containing secrets, or personal data. A maintainer will arrange a private
channel. Effective settings and subsequent changes are tracked in the repository settings
record.

Please include the commit/version, OS and architecture, affected command, impact,
and a minimal reproduction with redacted sample data. Coordinate publication with
the maintainer. Report upstream firmware, iOS application and bridge-service
vulnerabilities to their maintainers through their published reporting channels.

## Trust boundaries

- Hooks execute locally with the agent's existing user permissions. Setup never
  approves tools, changes agent trust, or reads conversation transcripts.
- Local IPC is restricted to the current OS user. It is not a boundary against
  malicious software already running as that user. All CLI and host instances
  should use the same version; rolling mixed-version IPC is not supported.
- Phone credentials use DPAPI, Keychain or Secret Service. There is no plaintext
  fallback. Bridge acceptance does not prove that an iPhone displayed a pattern.
- Self-update accepts packages from this project's GitHub releases and verifies
  SHA-256 before extraction and each manifest-owned file before replacement.
  Checksums from the same publisher detect corruption, not a compromised publisher.
  Packages are not independently code-signed. Keep the previous installation backup
  until startup and hardware behavior have been checked.
- Use a dedicated application directory. Stop the host and other application
  instances, run updates from outside that directory, and avoid editing its files
  while an update is pending. User-added files are copied during preparation;
  later changes remain in the retained backup and require manual reconciliation.

## Maintainer controls

Actions use pinned revisions and read-only permissions by default, with scoped
write permissions for publication and scanning. Weekly Dependabot proposals,
CodeQL and PR dependency review are configured in source. Their effective status,
repository-level controls and any access limitations are documented in
[the settings record](docs/repository-settings.md). Security findings require
triage; a green build is not proof of firmware compatibility or absence of flaws.
