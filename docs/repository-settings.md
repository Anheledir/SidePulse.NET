# Repository configuration record

Reviewed 2026-10-23. [Machine-readable observed and desired values](repository-settings.json).
This record distinguishes source configuration in PR #12 from effective GitHub
settings. The implementation has not been merged or released.

## Effective settings observed through GitHub

The repository is public, with `main` as its default branch. Issues and Wiki are
on; Projects is off. Merged branches are deleted automatically. Squash and rebase
merges are allowed, merge commits and auto-merge are disabled. The description
still says "Concept stage", homepage is empty, and no topics are set.

The authenticated user has an admin role, but the connected GitHub App excludes
repository administration. Branch-protection reads returned 403; security feature
states were not exposed. These controls are **unknown**, not disabled. No remote
admin settings have been changed in this review.

## Source configuration

- Weekly Dependabot updates for NuGet and Actions already exist.
- Security workflow adds C#, Python and Actions CodeQL analysis on PRs, main
  pushes and weekly schedule. C# uses an explicit .NET 10 build. Analysis needs
  repository code-scanning availability and a successful Actions run.
- PR dependency review fails on newly introduced high/critical advisories.
  It does not replace checking existing dependencies or advisory triage.
- All third-party Actions are pinned. Default permissions are `contents: read`;
  only jobs that need publication or SARIF upload receive write permissions.
- The initial native review-fix CI passed 96 tests per OS and dependency review.
  All three CodeQL analyses ran; the combined check reported one high-severity
  Zip Slip alert. Canonical containment hardening is being reanalyzed. Scan-job
  success alone is not a clean CodeQL result. Check names are recorded in JSON.
- SECURITY.md supplies reporting guidance and documents updater/IPC trust limits.
- Wiki publishing and upstream review run only from the default branch.

## Admin changes to apply and verify

Use the description, homepage and topics in the JSON record. Enable repository
Projects if a board is wanted. A repository feature switch does not create a
GitHub Projects board: create **SidePulse.NET delivery**, with description
"Cross-platform SidePulse.NET delivery, compatibility evidence and release gates",
and Todo, In progress, Validation and Done statuses; add #1 through #11 and PR #12.
Keep unfinished hardware and provider validation in Validation, not Done.

Enable private vulnerability reporting, Dependabot alerts/security updates,
secret scanning and push protection where available. Check Actions defaults are
read-only and workflow PR approval is off. Retain existing merge choices.

Protect main with PRs, resolved conversations, current checks, and blocked branch
force-push/deletion. For this personal repository use zero required approvals so
the sole maintainer is not locked out by GitHub's self-approval restriction.
Require exact successful check contexts only after they have run: documentation,
all three native build jobs, CodeQL for C#/Python/Actions, and dependency review.
Do not invent check names or enforce checks that have never completed.

Record each applied change, observed value and date here and in JSON. If an option
is unavailable, record the actual GitHub limitation rather than claiming success.
Do not create a long-lived PAT merely to work around the connector's admin scope.

## Reference

- [GitHub security and analysis settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-security-and-analysis-settings-for-your-repository)
- [CodeQL manual builds](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/manage-your-configuration/codeql-for-compiled-languages)
- [Dependency review action](https://github.com/actions/dependency-review-action)
