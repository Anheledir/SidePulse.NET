# Architecture

Status: implemented preview boundaries plus explicit future decisions. The solution
contains Core and CLI projects, with foreground/detached host commands and
current-user IPC. See [[Getting-Started]] and the PR #12 review for limitations.

## Boundaries

| Component | Responsibility |
|---|---|
| Core | Events, session state, deterministic reduction, output arbitration |
| Application | Pairing, device/integration management, diagnostics and migration use cases |
| Transports | USB files, SidePulse bridge, relay and platform adapters |
| Host | Background lifetime, local IPC, persistence and output scheduling |
| CLI | Spectre.Console.Cli commands and Spectre.Console presentation |
| Tests | Reducer, protocol, adapter contracts, rendered output and hardware smoke checks |

Hook-driven output is serialized by the host. Direct `state`, `write` and animation
apply commands send independently and can overwrite a running host's pattern.
They are explicit one-shot operations, not clients of a global output arbiter.
Stop the host before manual device checks. Coordinating direct writers with a
running host remains a decision in #5; do not claim exclusive device ownership.
The Application/Transports rows describe logical responsibilities, not additional
projects; migration, relay and persistence in this table are future work.

## Events and aggregation

Track session ID, integration, source machine, project label, event time and
last observation. Use explicit lifecycle events and ignore out-of-order events.
A finished subagent does not finish its parent session. Cancellation, agent errors,
recoverable tool errors and loss of observation remain distinguishable.

Implemented aggregation: waiting for user input takes priority, followed by active
work; show unhandled errors when nothing is actively working; completion only
when no tracked session remains active. Idle and unknown are separate states.
Tests cover the reducer; validate the policy further with real provider traces.

## Local event path

Hook adapters enqueue minimal metadata and return quickly. The host handles
network I/O asynchronously through a bounded latest-state queue. Repeated events
can retry delivery; there is no durable retry queue or separate event deduplication.
On Windows use current-user-restricted named pipes; do not open a network listener
just to receive local hook events. Document protocol versioning and startup races.

## Failure and restart behavior

Restart currently begins Unknown with an empty session table; no session metadata
is persisted. Do not infer successful completion from an expired heartbeat.
Protocol negotiation, restart metadata and session retention are tracked in #5.
The output queue is bounded and can drop older states while keeping the latest.
Undelivered output is logged, but there is no per-session delivery history.
