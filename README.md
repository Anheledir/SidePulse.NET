# SidePulse.NET

An unofficial .NET companion for SidePulse, with a Spectre.Console terminal interface.

**Status: first C# implementation preview. Feature parity and hardware validation are still in progress.**

Start with [Getting started](docs/wiki/Getting-Started.md) for build, USB, phone delivery and Copilot CLI instructions.

SidePulse.NET brings the SidePulse workflow to Windows, macOS and Linux:
pair a phone or connect a USB device, enable an agent integration, and see when
an agent is working, waiting for input or finished.

## Planned experience

- A live terminal dashboard for devices, agent sessions and recent events.
- Fast, silent commands for hooks and scripts, alongside interactive setup.
- Native Copilot CLI hooks, followed by the other upstream integrations.
- USB LED programs, iPhone linking and bridge delivery, animation previews and profiles.
- Reliable session aggregation, device aliases, diagnostics and explicit configuration migration.
- Self-contained Windows, macOS and Linux distributions without a Python dependency.

The proposed baseline is **C# / .NET 10 LTS**, **Spectre.Console** and
**Spectre.Console.Cli**. Windows, macOS and Linux are supported by the preview;
see the [platform matrix](docs/wiki/Platforms.md) for native credential requirements
and architecture-specific validation.

## Documentation

Read the published [project wiki](https://github.com/Anheledir/SidePulse.NET/wiki).
The editable source of truth lives in [docs/wiki](docs/wiki/Home.md).

- [Product concept](docs/wiki/Product-Concept.md)
- [Architecture](docs/wiki/Architecture.md)
- [Terminal experience](docs/wiki/Terminal-Experience.md)
- [Upstream compatibility inventory](docs/wiki/Compatibility.md)
- [Copilot integration](docs/wiki/Copilot-CLI.md)
- [Roadmap and acceptance criteria](docs/wiki/Roadmap.md)
- [Repository and automation setup](docs/wiki/Project-Setup.md)

## Original project and attribution

This project is inspired by and plans to port functionality from
[**inteliwear/sidepulse**](https://github.com/inteliwear/sidepulse), created by
**Peter Kuhar / InteliWEAR**, and the [SidePulse hardware](https://sidepulse.io/).
It is independently maintained and is not an official InteliWEAR product.

The reference baseline is upstream commit
[`ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb`](https://github.com/inteliwear/sidepulse/commit/ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb),
checked on 2026-10-23. Upstream is MIT licensed. See [NOTICE.md](NOTICE.md) and
the retained [upstream license](third-party/inteliwear-sidepulse/LICENSE).
Status LED profiles and protocol behavior are adapted from this baseline; see the provenance notice.

## Development

```shell
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/SidePulse.Cli -- dashboard
```


The solution contains a portable core, a Spectre.Console CLI and behavioral tests.
Implemented commands: `devices`, `state`, `write`, `dashboard`, `host`,
`hook-config`, `doctor` and `link pair/list/import/default/rename/remove`. Use `--help` for details and `--dry-run` before sending a program.
The command name `sidepulse-net` coexists with the existing Python port.

QR pairing and protected token storage use DPAPI on Windows, Keychain on macOS
and Secret Service on Linux. A persistent
service, full DSL validation, other upstream integrations and a signed installer
remain planned. The wiki's
concept pages describe the intended complete product; the getting-started page
records what is implemented now.

CI builds and tests Windows, Linux and macOS; CI publishes self-contained x64 and ARM64
preview archives for all three platforms. Wiki publishing uses the built-in Actions token. A weekly
upstream check opens or updates a review issue when the original repository
changes. See [automation setup](docs/wiki/Project-Setup.md).

Documentation checks:

```shell
python scripts/check_docs.py
python -m unittest discover -s scripts/tests -v
```

Contributions: [CONTRIBUTING.md](CONTRIBUTING.md). License: [MIT](LICENSE).
