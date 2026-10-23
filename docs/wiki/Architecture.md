# Architecture

Status: proposed; no application components have been implemented.

## Boundaries

| Component | Responsibility |
|---|---|
| Core | Events, session state, deterministic reduction, output arbitration |
| Application | Pairing, device/integration management, diagnostics and migration use cases |
| Transports | USB files, SidePulse bridge, relay and platform adapters |
| Host | Background lifetime, local IPC, persistence and output scheduling |
| CLI | Spectre.Console.Cli commands and Spectre.Console presentation |
| Tests | Reducer, protocol, adapter contracts, rendered output and hardware smoke checks |

The UI never owns device transport state. All writers submit intents to one host,
so a background service, dashboard and hook cannot independently overwrite LEDs.
A foreground-only mode should be possible for diagnosis and restricted machines.

## Events and aggregation

Track session ID, integration, source machine, project label, event time and
last observation. Use explicit lifecycle events and ignore out-of-order events.
A finished subagent does not finish its parent session. Cancellation, agent errors,
recoverable tool errors and loss of observation remain distinguishable.

Proposed aggregation: waiting for user input takes priority, followed by active
work; show unhandled errors when nothing is actively working; completion only
when no tracked session remains active. Idle and unknown are separate states.
The exact policy is a design decision to validate with realistic event traces.

## Local event path

Hook adapters enqueue minimal metadata and return quickly. The host handles
network I/O, deduplication, bounded retries and transport diagnostics asynchronously.
On Windows use current-user-restricted named pipes; do not open a network listener
just to receive local hook events. Document protocol versioning and startup races.

## Failure and restart behavior

Persist enough metadata for restart recovery without storing prompts or tool
arguments. After restart, stale work becomes unknown until refreshed by evidence.
Do not infer successful completion from an expired heartbeat. Use bounded queues
and expose dropped events or undelivered outputs in diagnostics.
