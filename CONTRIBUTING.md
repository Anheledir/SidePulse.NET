# Contributing

SidePulse.NET is currently in concept review. Discuss significant design choices
before implementation. Keep the application free of C# scaffolding until the
concept and first milestone are approved.

## Changes and reviews

Use a branch and a pull request against `main`. Explain the user-visible change,
its validation and any hardware limitations. Keep unrelated changes separate.
Update wiki sources in `docs/wiki` in the same pull request as behavior changes.
The main repository is the documentation source of truth; direct wiki edits need
to be brought back into it before the next automated publication.

## When implementation starts

Use .NET 10 LTS and Spectre.Console for presentation. Keep state reduction,
protocol logic and device transports independent of the terminal UI. Introduce
central package management and lock files with the first solution. Add meaningful
unit, transport-contract, hook-failure and hardware smoke tests as those areas arrive.

Rendered, interactive output is for people. Hook and JSON output must have no
ANSI sequences, prompts or accidental diagnostic text. Test Windows paths with
spaces and Unicode, concurrent sessions and offline phone delivery.

Run the documentation check before submitting:

```shell
python scripts/check_docs.py
```

Retain upstream notices when copying code or assets. The upstream compatibility
matrix distinguishes planned, implemented and hardware-verified behavior.
