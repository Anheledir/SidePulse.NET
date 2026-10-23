# Upstream compatibility inventory

Reference: inteliwear/sidepulse at
[ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb](https://github.com/inteliwear/sidepulse/commit/ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb),
checked 2026-10-23. The inventory below records the full planned scope. The preview already implements
firmware size limits, writable USB files, iPhone pairing/silent delivery, status
profiles, Copilot hooks, in-memory session aggregation and native credential
storage. Full LED grammar, visible notifications, broadcast and remaining
integrations remain planned. See [[Getting-Started]] and [[Platforms]] for
implemented commands and test boundaries.

| Area | .NET plan | Verification required |
|---|---|---|
| LED text language and limits | M1 | Port upstream vectors, escapes, 512-byte/20-line boundaries |
| Writable USB volumes and discovery | Preview, all three OSes | Dot hardware, volume labels, locked/removed media |
| iPhone QR/token pairing and linked targets | M1 | Original app, production/development tokens, persistence |
| LED bridge delivery and visible notifications | M1 | Silent versus visible delivery, timeouts and failure semantics |
| Destination selection, stdin and broadcast | M1 | Compatible ambiguity checks and explicit broadcast |
| Built-in animations and LED counts | M1 | Source attribution, Dot/Pro geometry and brightness |
| Copilot CLI hooks | Preview, Bash and PowerShell | Event fixtures and real CLI on each OS |
| Codex, Claude Code, Kiro CLI V3 | Preview | Native schema fixtures and simulated wrapper execution; real agent checks pending |
| Cursor, Grok, Junie and other providers | M2 parity | Separate hooks and manual verification |
| Session collection, history and aggregation | M1/M2 | Concurrent sessions, stale events and restart |
| Optional transcript recovery | M2 investigation | Privacy, format drift and opt-in behavior |
| Background host and service management | M1 Windows | Named-pipe access, foreground mode and login startup |
| Remote machine relay | M2 | Channel capability handling and event deduplication |
| Android pairing/push | M2 | Separate token format and real Android app |
| Settings and profile migration | M1/M2 | Preview, backup, source version and rollback |
| Battery indication | M2 Windows adaptation | Windows battery API; macOS power/PD fields differ |
| Firmware query/update | Investigation | Device recovery and source-specific update transport |
| Read-only/raw USB control and status channel | Investigation | Windows driver requirements and hardware tests |
| macOS menu bar and settings UI | Platform-specific deferred | Spectre terminal dashboard does not reproduce AppKit UI |
| SD eject guard and lid/sleep helpers | macOS-specific deferred | Separate OS-specific feasibility decision |
| Local MLX reply classifier | Apple-specific optional/deferred | Not required by core agent status flow |
| Installer/update/distribution | M1/M3 adaptation | Coexistence, integrity, uninstall and later signing |

The existing Python Windows port and Copilot add-on are useful reference work;
their test counts and user hardware reports do not verify the future .NET port.
Do not call the project feature-complete until each applicable row is implemented
and its validation is recorded. Any copied fixtures/assets must retain upstream notices.
