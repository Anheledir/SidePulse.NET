# SidePulse.NET

An unofficial .NET companion for SidePulse, with a Spectre.Console terminal interface.

**Status: concept and project preparation. No C# application, downloads or feature-complete port exist yet.**

SidePulse.NET will bring the SidePulse workflow to a native Windows application:
pair a phone or connect a USB device, enable an agent integration, and see when
an agent is working, waiting for input or finished.

## Planned experience

- A live terminal dashboard for devices, agent sessions and recent events.
- Fast, silent commands for hooks and scripts, alongside interactive setup.
- Native Copilot CLI hooks, followed by the other upstream integrations.
- USB LED programs, iPhone linking and bridge delivery, animation previews and profiles.
- Reliable session aggregation, device aliases, diagnostics and explicit configuration migration.
- A self-contained Windows distribution without a Python dependency.

The proposed baseline is **C# / .NET 10 LTS**, **Spectre.Console** and
**Spectre.Console.Cli**. Windows is the first delivery target. Platform-specific
features and later Linux/macOS support are tracked individually.

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
No upstream application code or animation assets have been copied into this initial setup.

## Development

Application implementation has not started. Review the concept and compatibility
inventory before creating the solution. Future commands shown in the wiki are
proposals, not currently executable instructions.

The initial repository contains documentation checks, conditional .NET CI,
optional wiki publication, Dependabot configuration, issue templates and a
backlog of [11 planning issues](https://github.com/Anheledir/SidePulse.NET/issues).
It does not claim a passing application build or test suite.

Run the current checks with Python 3:

```shell
python scripts/check_docs.py
```

Contributions: [CONTRIBUTING.md](CONTRIBUTING.md). License: [MIT](LICENSE).
