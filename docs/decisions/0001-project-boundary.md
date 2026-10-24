# ADR 0001: Independent .NET companion

Status: accepted by the maintainer in the project discussion, 2026-10-23.

Build SidePulse.NET as an independently maintained, cross-platform .NET 10 LTS
application using Spectre.Console and Spectre.Console.Cli. Preserve compatible
wire behavior where feasible, while tracking platform-specific limitations.

The concept review is complete and implementation is in PR #12. The working
Python port and its hook add-on remain separate reference implementations.

This permits an idiomatic C# architecture without claiming drop-in compatibility
for all macOS-specific helpers. Upstream attribution and license retention are
requirements for every later import.
