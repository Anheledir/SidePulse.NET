# Roadmap

All application work is planned. Only the initial documentation and automation
scaffolding are prepared.

## M0: Concept and project foundation

Review product scope, terminal workflow, architecture and compatibility matrix.
Create repository/wiki, documentation checks and issue templates. No C# scaffolding
until concept approval. Complete when open design decisions are recorded and the
first implementation milestone is accepted.

## M1: Useful Windows companion

Implement the LED language and transport contracts, direct USB writes, iPhone
pairing/delivery, manual state commands, shared host, Copilot CLI hooks and a
Spectre dashboard. Add aliases and basic diagnostics.

Acceptance: from a clean Windows install, pair the original iOS app, observe a
real Copilot work/wait/turn-end cycle, and exercise a directly connected Dot.
Overlapping sessions do not show finished while another is working. Offline bridge
handling does not block hooks. Command/JSON/hook output stays machine-readable.

## M2: Upstream coverage and quality of life

Port remaining agent adapters, remote relay, Android integration, profiles,
battery adaptation and optional migration/recovery features. Record compatibility
and platform-specific exclusions for every upstream inventory row.

## M3: Distribution and hardening

Provide self-contained Windows x64/arm64 artifacts, checksums and installation/
uninstallation documentation. Introduce release automation only after real build,
test and publish paths exist. Evaluate signing, package-name availability and
package-manager distribution. Linux/macOS support has its own acceptance gates.

## Investigation tracks

Firmware safety/recovery, read-only/raw USB support, macOS-specific helpers and
an optional local classifier each need an explicit feasibility decision.

Prepared issue candidates are in `docs/backlog.json`. They are planning items,
not a promise that implementation has already started.
