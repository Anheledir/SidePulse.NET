# ADR 0001: Independent .NET companion

Status: proposed, 2026-10-23.

Build SidePulse.NET as an independently maintained, Windows-first .NET 10 LTS
application using Spectre.Console and Spectre.Console.Cli. Preserve compatible
wire behavior where feasible, while tracking platform-specific limitations.

The initial repository contains planning and automation only. Do not add C#
application scaffolding until concept review is complete. The working Python port
and its hook add-on remain separate reference implementations.

This permits an idiomatic C# architecture without claiming drop-in compatibility
for all macOS-specific helpers. Upstream attribution and license retention are
requirements for every later import.
