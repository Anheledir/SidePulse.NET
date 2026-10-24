# Agent integrations

The preview supports Copilot CLI, current local Codex lifecycle hooks, Claude Code
Kiro CLI V3 standalone hooks and OpenCode local plugins. They feed one current-user host and share the
same status reducer. Provider IDs namespace sessions, so equal session IDs from
different tools cannot collide. `agents list --json` reports supported events
and limitations. Unknown providers/events are not guessed from terminal text.

## Setup, inspection and removal

```shell
sidepulse-net agents list
sidepulse-net agents install copilot --dry-run
sidepulse-net agents install copilot
sidepulse-net agents install codex
sidepulse-net agents install claude
sidepulse-net agents install kiro
sidepulse-net agents install opencode --dry-run
sidepulse-net agents install opencode
sidepulse-net agents status claude --json
sidepulse-net agents uninstall claude --dry-run
sidepulse-net agents uninstall claude
```

Run the published executable, or supply its absolute path with `--exe`.
Install defaults to the provider's user configuration. Use `--config <PATH>` for
an explicitly chosen project-scoped or isolated provider file. This is a file
path, not a directory. Do not register the same integration at user and project
scope unless duplicate events are intended.

| Provider | Default file | Integration boundary |
|---|---|---|
| Copilot CLI | `$COPILOT_HOME/hooks/sidepulse-net.json` or `~/.copilot/hooks/sidepulse-net.json` | Dedicated version-1 hook file; Bash and PowerShell wrappers |
| Codex | `$CODEX_HOME/hooks.json` or `~/.codex/hooks.json` | Merge lifecycle groups into the hooks object |
| Claude Code | `~/.claude/settings.json` | Merge lifecycle groups while preserving unrelated settings |
| OpenCode | `$OPENCODE_CONFIG_DIR/plugins/sidepulse-net.js` or `$XDG_CONFIG_HOME/opencode/plugins/sidepulse-net.js` (default `~/.config/opencode/plugins/sidepulse-net.js`) | Dedicated local JavaScript event plugin; no npm dependency |
| Kiro CLI V3 | `~/.kiro/hooks/sidepulse-net.json` | Dedicated version-v1 standalone hook document |

Installation is explicit. Dry runs show only the new managed fragment and target,
never the rest of an existing settings file. Shared settings are backed up byte
for byte before being updated. Backups have a `.bak` suffix so agents do not load
them as hook files. SidePulse.NET stores a separate installation manifest with
before/after hashes. Repeated installation is idempotent; uninstall then install
to change the executable path or generated hook definitions.

Uninstall restores the exact original file, or removes a dedicated file created
by SidePulse.NET. It refuses to overwrite files edited after installation or to
restore a missing/modified backup. Review and merge those cases manually. A
pending installation interrupted before its target write can be removed safely.
Backups remain available after removal. Do not delete them before uninstalling.
Malformed JSON, unsupported hook shapes and unowned dedicated files are refused.
OpenCode installs a JavaScript file instead of JSON; use
`--config .opencode/plugins/sidepulse-net.js` for project scope. It uses a directly
spawned executable on every platform, without Bash or PowerShell.
The installer preserves permission settings and never approves tools, bypasses
trust, or edits agent instructions. Restart the agent after installing/removing.
Codex requires reviewing the new definitions in `/hooks` before they execute.

## State mapping

Prompt submission and tool activity indicate working; explicit root turn-stop
signals indicate done. Codex approval requests and supported Claude/Copilot input
notifications indicate waiting. Claude tool/turn failures and Copilot error
signals indicate error. Codex interrupt and session-end events indicate idle.
Kiro exposes working, idle and completed-turn signals in this adapter; no general
permission-wait or error event is inferred. Tool failure does not automatically
mean the entire task failed, and done does not establish correctness.

Subagent and background completion events never finish root sessions. Child-agent
payloads with an explicit `agent_id` are ignored by the root adapter. A session
start caused by compaction does not clear active root work. Waiting wins over
working, then unhandled errors, completed turns and idle. A stop after an error
preserves that error until activity resumes or the session ends.

Copilot uses its documented event timestamp. Other adapters use ingestion time
because their common payloads do not guarantee timestamps. Concurrent hooks may
arrive out of order. There is no transcript parsing, prompt classification or
account/API access. Missing session identities are ignored rather than combining
unrelated activity under a placeholder. The IPC message contains only provider,
session identity, state and timestamp; prompt and tool contents are discarded.

Generated wrappers are silent and return zero on runtime failures. Hook commands
have bounded input and connection budgets. Windows command-only provider formats
use a PowerShell 7 encoded command to avoid parent-shell quoting ambiguities;
Copilot includes its native PowerShell field. Unix uses quoted shell commands.
These are passive status hooks: their output never contains policy decisions.

## OpenCode

```shell
sidepulse-net agents install opencode
sidepulse-net host start --phone default
sidepulse-net agents status opencode --json
sidepulse-net agents uninstall opencode --dry-run
```

Restart OpenCode after installation. The plugin subscribes to native events:

| Event | SidePulse state |
|---|---|
| `session.status` busy/retry | Working |
| `permission.asked`, `question.asked` | Waiting |
| Last pending permission/question replied or rejected | Working |
| `session.error` | Error |
| Session created/deleted or status idle | Idle |

An error remains visible through a following idle event until activity resumes
or the session is deleted. Pending requests are tracked separately per session and request. Busy/retry events
and individual replies cannot clear another pending prompt. Child sessions keep
their own identity and cannot finish a parent. Idle may also follow cancellation,
so this adapter deliberately does not infer Done. A terminated OpenCode process
has no guaranteed shutdown event; use `host reset` to clear abandoned sessions.
Restarting the plugin does not reconstruct pending requests from earlier events.

The plugin forwards only session identity and status, never prompt text, question
contents, permission rules or tool arguments. Child processes are silent, shell-free
and bounded to 1.5 seconds. A missing host or executable does not interrupt the agent.
It never answers a permission request or changes OpenCode configuration/trust policy.
Local plugin loading and event shapes are based on the official plugin reference
and upstream v1.2.27 session, permission and question schemas. Synthetic native CI
runs exercise the generated plugin; actual OpenCode/version acceptance remains required.

## Validation and limits

Tests cover representative documented payloads, each provider's configuration
shape, mixed-provider sessions, configuration preservation and generated command
execution through Bash or PowerShell. CI uses synthetic events and no paid agent
accounts. Actual agent installations still need manual checks, especially hooks
that other plugins can delay or continue. A Stop hook can run before another hook
requests continuation; subsequent activity returns the signal to working.

Kiro CLI 2.x agent profiles, legacy Codex notification-only releases, remote/cloud
agent orchestration and other providers (Cursor, Gemini CLI, Grok, Junie, etc.)
remain separate adapters to implement and verify. The adapter registry and common
installer are intended to make those additions straightforward.

## Protocol references

Checked 2026-10-23:

- [Copilot hook reference](https://docs.github.com/en/copilot/reference/hooks-reference)
- [Codex lifecycle hooks](https://developers.openai.com/codex/hooks)
- [Claude Code hooks](https://code.claude.com/docs/en/hooks)
- [OpenCode plugins](https://opencode.ai/docs/plugins/)
- [OpenCode session status schema](https://github.com/anomalyco/opencode/blob/v1.2.27/packages/opencode/src/session/status.ts)
- [OpenCode permission events](https://github.com/anomalyco/opencode/blob/v1.2.27/packages/opencode/src/permission/service.ts)
- [OpenCode question events](https://github.com/anomalyco/opencode/blob/v1.2.27/packages/opencode/src/question/service.ts)
- [Kiro standalone schema](https://kiro.dev/docs/hooks/)
- [Kiro triggers and session identity](https://kiro.dev/docs/hooks/types/)
