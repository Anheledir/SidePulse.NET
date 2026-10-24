# Scripting and interactive menus

In a terminal, `sidepulse-net` opens an arrow-key menu. Bare groups `animations`,
`profiles`, `agents`, `host`, `link`, and `about` open their respective menus.
Up/Down select, Enter continues, Escape goes back. Fully specified commands always
execute directly. The legacy `host --phone default` shorthand remains supported.

Guided actions display a single-line script command before running. Review offers
Run, Copy command, Choose shell and Back. PowerShell and Bash quoting preserve
literal values; CMD export rejects values it cannot safely represent. Clipboard
support uses the OS clipboard or installed `wl-copy` / `xclip`; otherwise copy the
printed line. Secrets are represented by phone aliases or environment variable
names, never token values. Editor commands reference the selected file; save it
before using a preview command elsewhere.

Menus require both stdin and stdout to be terminals. Redirecting either stream,
passing `--no-interactive`, `--quiet`, or `--json` prevents menus. Bare groups then
show command help. No arguments show help. Missing required arguments fail without
prompting. Editors require a terminal. QR pairing remains an explicit pairing
operation with its existing timeout; it is not a script configuration wizard.

Global flags may precede or follow subcommands:

- `--quiet`: suppress stdout, preserve stderr and exit status.
- `--json`: structured output on commands advertising JSON. Takes precedence over
  quiet. Runtime errors are JSON on stderr, never mixed with stdout data.
- `--no-interactive`: disable cursor menus and interactive playback.
- `--no-color`: disable ANSI colors. `NO_COLOR` and redirected stdout also disable
  color. A non-interactive preview prints a single RGB frame instead of animating.

Exit 0 indicates successful command execution, 1 indicates an error. Delivery
success means the OS or bridge accepted the write, not hardware acknowledgement.
Raw `write` retains envelope-only validation for forward compatibility; use
`animations validate` for grammar validation. Global flags are reserved before
`--`; literal values containing a flag should be passed after `--` where supported.

```sh
sidepulse-net animations validate --file build.LED --quiet
sidepulse-net animations preview --file build.LED --model dot --at 500 --json
sidepulse-net state Working --phone default --quiet
sidepulse-net devices --json
sidepulse-net about credits --json
sidepulse-net about licenses --full
sidepulse-net about github
sidepulse-net about github --open
sidepulse-net docs --offline
```

`about` includes manufacturer, project and upstream links, credits, dependency
versions, license identifiers, and offline license texts. Browser launch is opt-in
through `--open`. Offline animation/scripting documentation ships with previews.
The public wiki receives these pages after the implementation branch is merged.

The manufacturer's affiliate program currently requires individual approval. The
application uses ordinary manufacturer URLs; no referral identifier is fabricated.
An approved affiliate link would require clear disclosure next to it. See the
[official program](https://sidepulse.io/affiliates.html).
