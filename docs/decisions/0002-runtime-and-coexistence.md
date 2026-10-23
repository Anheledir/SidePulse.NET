# ADR 0002: Shared state owner and explicit migration

Status: accepted by the maintainer in the project discussion, 2026-10-23.

A local background host will own event reduction, per-session state and device
outputs. Hook adapters will enqueue small events over authenticated local IPC.
The Spectre dashboard and noninteractive commands will use the same state owner.

Windows named pipes scoped to the current user are the preferred initial IPC
transport. Transport details, restart recovery and latency budgets need a prototype.

Use `sidepulse-net` as the initial command to coexist with the existing Python
`sidepulse`. Offer a `sidepulse` alias only by explicit choice. Store new settings
under a separate SidePulse.NET directory; migration is previewed, opt-in and
backs up source configuration. Never silently replace existing hooks or tokens.
