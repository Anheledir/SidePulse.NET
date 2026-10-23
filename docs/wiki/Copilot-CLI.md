# Copilot CLI integration

Status: .NET design; an independent Python hook add-on already exists as reference.

## Hook locations

Current GitHub documentation describes user hooks in `%USERPROFILE%\.copilot\hooks`
on Windows, or `$COPILOT_HOME/hooks` when overridden. Repository hooks can coexist
in `.github/hooks`. Configuration is read when Copilot CLI starts.
Use a dedicated SidePulse.NET JSON file and back up any file it owns before replacing it.

## Proposed mapping

| Event | Meaning |
|---|---|
| userPromptSubmitted | Working |
| preToolUse / postToolUse | Tool activity / resume work |
| preToolUse for ask_user | Waiting for input |
| notification: permission_prompt | Actual permission prompt |
| notification: elicitation_dialog | Actual request for information |
| agentStop | Current main-agent turn ended |
| postToolUseFailure / errorOccurred | Tool/agent error with recovery context |
| sessionEnd | Session closed, preserving error/cancellation reason |

Do not mark waiting on every `permissionRequest`: it runs before policy and may
be automatically approved. Do not let `subagentStop` or a background shell-complete
notification falsely finish the main session. Green means a turn ended, not that
the output was correct or that tests passed.

## Hook behavior

Return promptly after local event submission; keep network traffic in the host.
No stdout, prompts, approval JSON or permission overrides. Failures in the observer
must leave the agent's normal execution and approval behavior intact. Test startup
races, service downtime, Unicode, PowerShell quoting and malformed payloads.

Only persist the metadata required for status. Prompts, tool arguments and full
transcripts are excluded by default. Support multiple simultaneous sessions from
the outset rather than allowing unrelated sessions to overwrite each other.

## Versions and surfaces

Capture real payload fixtures from the installed CLI before finalizing adapter
behavior. The documentation does not specify a single minimum version covering
all proposed events. Windows command hooks currently require PowerShell 7.
Copilot CLI and Visual Studio are separate integration surfaces; do not assume
that Visual Studio supports the CLI's hooks.

References:
- https://docs.github.com/en/copilot/reference/hooks-reference
- https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks
