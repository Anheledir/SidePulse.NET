# Upstream and licensing

The original [SidePulse project](https://github.com/inteliwear/sidepulse) is created
by Peter Kuhar / InteliWEAR and licensed under MIT. It defines the hardware-facing
behavior and the phone/bridge integration that this companion plans to reuse.

Baseline: commit `ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb`, reviewed 2026-10-23.
SidePulse.NET is independently maintained, not an official product or endorsement.
The initial project contains documentation and automation only.

Keep the original license at `third-party/inteliwear-sidepulse/LICENSE` and record
future code/asset imports in `NOTICE.md`, including the source commit and affected
files. A reimplementation does not imply that upstream source or animations have
already been copied. Do not use original branding artwork without an appropriate basis.

Planned dependencies include [Spectre.Console](https://spectreconsole.net/) and
Spectre.Console.Cli. Capture their exact versions/licenses when implementation
introduces them, together with other distributed dependencies.

Reference documents:
- https://sidepulse.io/
- https://github.com/inteliwear/sidepulse
- https://spectreconsole.net/
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://docs.github.com/en/copilot/reference/hooks-reference
