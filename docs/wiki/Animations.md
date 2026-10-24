# Animations and custom patterns

SidePulse Dot (USB-C) has two LEDs; SidePulse Pro (SD slot) has eight. Both use
upstream `LEDS.LED` syntax. Named patterns select a model-specific variant. Preview
defaults to Dot; apply detects a known mounted model or requires `--model`. Phone
output requires an explicit model. File transport still requires writable storage;
this feature does not add direct USB or SD read transports.

```sh
sidepulse-net animations list
sidepulse-net animations preview cyan-roll --model dot
sidepulse-net animations preview --file custom.LED --model pro
sidepulse-net animations preview --file custom.LED --both
sidepulse-net animations validate custom.LED --quiet
sidepulse-net animations edit --file custom.LED --model dot
sidepulse-net animations import personal --file custom.LED --model dot
sidepulse-net animations export personal --model dot --output exported.LED
sidepulse-net animations apply personal --model dot --phone default --brightness 40
```

The editor supports guided color, per-LED color/hold, off, roll, brightness,
duration, easing, delay, raw line editing and repeat settings. Preview with Space
(pause), Right (advance 100ms), R (restart) and Escape (back). Save before leaving;
unsaved changes can be discarded explicitly. Existing files are replaced only if
they have not changed since opening. Import/export refuse replacement unless
`--force` is supplied. Built-ins cannot be overwritten; choose a custom name.
Custom files are stored separately for Dot and Pro. Import both variants under the
same name when their layouts differ. Names use lowercase letters, digits, `_`, `-`.

The managed interpreter validates colors, indexes, duration/delay (up to 65535ms),
brightness 0..255, easing names, roll, finite/infinite repeats and the firmware's
512 UTF-8 byte / 20 physical line envelope. Indexed assignments preserve other
LEDs. A single color fills the strip; a color list clears trailing LEDs. Valid
out-of-range LEDs are ignored. Comments begin with `;`, `//` or `# `.

Preview is entirely Spectre.Console text and colored blocks. It starts from black;
hardware may retain previous colors. Terminal colors, refresh rate, easing curves
and rounding are approximations and have not been validated against firmware.
Preview does not establish device receipt. `--both` compares the model-specific variants of named patterns, or plays the
same custom file on both layouts.
Brightness percent scales program brightness; an additional directive consumes
one physical line, so a program at the envelope limit may need shortening.

Non-interactive preview emits one frame and exits immediately. `--at 500` selects
its time in milliseconds; `--json` emits model, timestamp and RGB values. Interactive
preview runs for 10 seconds by default; `--seconds` permits up to 600 seconds.

## Agent state profiles

```sh
sidepulse-net profiles set personal --state Working --pattern cyan-roll --brightness 40
sidepulse-net profiles set personal --state Waiting --pattern amber-pulse
sidepulse-net profiles show personal --json
sidepulse-net host start --phone default --profile personal --model dot
```

Unassigned states retain the original status patterns. Profile brightness applies
to all states. Profiles are loaded at host startup; restart the host after edits.
Every referenced pattern must have a variant for the selected model. Existing
running hosts retain their startup configuration; stop first to apply changes.

## Upstream credit

Bundled patterns are copied from `inteliwear/sidepulse` at the revision in
`upstream.json`, under its MIT license. See [[Upstream-and-Licensing]]. The managed
interpreter is a new implementation from the public format specification.
