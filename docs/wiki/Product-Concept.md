# Product concept

## Goal

Make agent activity visible without keeping an agent window in focus. The first
successful journey is install, pair an iPhone or connect a Dot, enable Copilot CLI
hooks, and see working / waiting / finished states.

## Two interfaces

The interactive terminal experience uses Spectre.Console tables, panels,
selection prompts and live views. Noninteractive commands are short-lived,
scriptable and predictable, with structured JSON and plain output modes.
A background host coordinates sessions and outputs.

## First milestone

Self-contained Windows, macOS and Linux distributions; direct writable USB devices; iPhone
pairing and bridge delivery; core LED language; manual state commands; Copilot CLI
integration; a device/session dashboard; and diagnostics.

## Quality-of-life goals

Device aliases and per-device defaults; brightness and animation profiles;
preview-before-import migration from the Python port; inspect/repair/remove hooks;
multiple simultaneous sessions; and clear offline/unknown status.
Do not claim phone receipt when only the bridge accepted a message.

## Scope boundaries

No cloud account system of our own, no requirement to change agent prompts to
make hooks work, and no automatic grants of agent permissions. An AI classifier
is optional upstream functionality, not a first-milestone dependency.
Firmware and raw USB parity require a dedicated compatibility investigation.

## Decisions awaiting concept approval

Confirm dashboard interaction details, packaging/name availability, service
lifetime and startup behavior, aggregation policy and how long completion stays
visible. .NET 10 LTS and separate `sidepulse-net` command are the proposed defaults.
