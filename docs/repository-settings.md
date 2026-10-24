# Repository configuration record

Verified 2026-10-24. [Machine-readable settings and change log](repository-settings.json).
The implementation is in PR #12 and has not been merged or released.

The GitHub connector excludes administration mutations. The requested admin work
was completed through the signed-in browser; ruleset creation required fresh
GitHub verification. Saved values were checked in the UI, metadata through the
repository API, and the active ruleset through the ruleset API. No PAT was created.

## Changes applied

| Setting | Effective value |
|---|---|
| Description | Unofficial cross-platform SidePulse companion in C# and Spectre.Console: agent status hooks, LED patterns, iPhone pairing and verified updates. |
| Homepage | Project wiki |
| Topics | sidepulse, dotnet, csharp, spectre-console, cli, cross-platform, github-copilot, codex, claude-code, kiro, led |
| Projects | Enabled; delivery board created and linked |
| Dependency malware alerts | Enabled |
| Actions sources | Owner and GitHub-created actions only; no broad Marketplace allowance |
| Actions revision policy | Full commit SHA required |
| External fork workflows | Approval required for all external contributors |
| Main protection | Active ruleset 25761788, described below |

## Verified existing controls

Private vulnerability reporting, dependency graph, Dependabot alerts/security
updates and grouped security updates were already on. Secret scanning and push
protection were already on. Releases were already immutable. Wiki editing was
already restricted to collaborators. Actions default token access was already
read-only, and Actions creation/approval of pull requests was disabled.

CodeQL uses advanced setup. Its combined check fails for high-or-higher security
alerts and standard errors. Optional AI Scan and credit-consuming agentic autofix
remain off; existing Copilot Autofix is on. Automatic dependency submission is off.

The repository remains public, `main` remains default, squash/rebase merges are
allowed, merge commits and auto-merge are off, and merged branches are deleted.
Existing web-commit signoff remains on. General log/artifact retention is 90 days;
preview packages request 14 days in the workflow. Cache retention/size remain
7 days/10 GB. No account, ownership or collaborator permissions were changed.

## Main delivery checks

[Active ruleset](https://github.com/Anheledir/SidePulse.NET/settings/rules/25761788)
targets the default branch, currently `main`, with an empty bypass list:

- Require a pull request, resolved conversations and an up-to-date tested branch.
- Block branch deletion and force pushes.
- Require `documentation`, `build (windows-latest)`, `build (ubuntu-latest)`,
  `build (macos-latest)`, `CodeQL (csharp)`, `CodeQL (python)`, `CodeQL (actions)`,
  `Dependency review`, and the combined `CodeQL` alert check.
- Bind the combined alert check to GitHub Advanced Security (integration 57789)
  and the eight workflow checks to GitHub Actions (integration 15368).
- Require zero approvals for this solo-maintainer project. GitHub prevents
  self-approval; the PR/check/conversation requirements still apply.

The existing **Main** ruleset 25757011 was disabled. It remains untouched and
disabled. Its signed-commit/code-quality/Copilot requirements were not silently
activated. The new rule requires the observed combined CodeQL check rather than
requiring scanning results on both the implementation and the unscanned default
branch, which would block the first implementation merge.

Future branch-policy changes should preserve the distinction between scan-job
completion and a clean alert result. Required check names were observed in
successful runs, not invented. Repository merge settings still exclude merge
commits even though the ruleset's allowed-method intersection includes them.

## Delivery project

[SidePulse.NET delivery](https://github.com/users/Anheledir/projects/5/views/1)
is a private owner project linked to this public repository. It has a Delivery
board, short description and README explaining scope, status meanings, validation
requirements and attribution. It imports open issues and PRs from this repository.

| Column | Items at verification |
|---|---|
| Todo | #11 |
| In Progress | #5, #7, #8, #9 |
| Validation | #2, #3, #4, #6, #10, PR #12 |
| Done | #1, concept approval only |

All eleven issues and PR #12 are represented. Incomplete implementation and
physical/provider/release acceptance remain visible instead of marking them done.
The board remains private; repository visibility was not changed.

## Source configuration and evidence

Weekly Dependabot updates for NuGet and Actions already exist. PR #12 adds
SECURITY.md, C#/Python/Actions CodeQL on PRs/main/weekly schedule, and PR dependency
review rejecting new high/critical advisories. Third-party Actions are pinned;
workflow permissions are read-only by default with scoped publication/scanning
exceptions. Wiki/upstream publishing runs only from the default branch.

Reviewed code `f1ada51` passed **104 tests per OS** in
[native CI](https://github.com/Anheledir/SidePulse.NET/actions/runs/40039767890),
including updater replacement and rollback. Quality and dependency review passed.
[Security analysis](https://github.com/Anheledir/SidePulse.NET/actions/runs/40039767819)
passed all three languages; the combined CodeQL check reports no new alerts after
canonical containment hardening. No rule was suppressed or alert dismissed.

The audit/settings commits change documentation only. Check the PR head's required
checks before merging. Source documentation/wiki/security-policy changes become
default-branch documentation only after merge. Runtime/hardware/provider release
gates remain in the issues and [QA review](reviews/pr-12.md).

## References

- [GitHub security settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-security-and-analysis-settings-for-your-repository)
- [CodeQL manual builds](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/manage-your-configuration/codeql-for-compiled-languages)
- [Dependency review action](https://github.com/actions/dependency-review-action)
