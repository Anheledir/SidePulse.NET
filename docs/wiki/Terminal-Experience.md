# Terminal experience

Status: design proposal. There are no application screenshots yet.

## Visual direction

Calm, readable and compact: neutral borders, cyan activity, amber input requests,
green completion and red errors. Colour always has a text label and optional icon.
Use native terminal colours and capability detection instead of assuming truecolour,
Unicode or an 80-column window. Escape untrusted text before Spectre markup rendering.

## Dashboard

A header shows host health and the active profile. Device rows show aliases,
transport, observed state and last successful transmission. Session rows show
project, integration, activity and last observation. A bounded event list explains
changes and delivery failures. Expanded details reveal raw metadata only on request.

Render with Spectre.Console live output. Whether navigation uses a selection-menu
shell or a fully live key-driven view needs a prototype; Spectre.Console is not a
complete desktop-style terminal UI framework.

## Main areas

- Overview: device/session activity and recent events.
- Devices: pairing QR/token, aliases, test pattern and connection diagnosis.
- Integrations: install, doctor, repair and remove hooks.
- Animations: select, preview, edit, validate and assign per-state programs.
- Settings: profiles, brightness, startup and opt-in migration.

## Proposed commands

```powershell
spn
spn link
spn state working
spn devices list --json
spn integrations install copilot
spn doctor
```

These commands are proposals, not installation instructions. Reserve `--quiet`,
`--json`, `--no-color` and `--non-interactive`. Redirected stdout must not start
an interactive dashboard. Hook commands never prompt and keep stdout empty.
