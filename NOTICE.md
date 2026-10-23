# Attribution and provenance

SidePulse.NET is an unofficial companion and planned C# reimplementation of
functionality from https://github.com/inteliwear/sidepulse.

Original SidePulse software: copyright (c) 2026 Peter Kuhar, MIT license.
Reference baseline: ffb1d3c2f3eed3b7e80e32d7876a7e1d67bd5dfb, reviewed 2026-10-23.
The original license is preserved in `third-party/inteliwear-sidepulse/LICENSE`.

The initial commit contains new planning documentation and repository automation.
It contains no copied upstream application source, firmware or animation assets.
When upstream code or assets are ported or copied, preserve their notices and
record the source revision and affected files here.

The SidePulse name identifies compatibility with the original hardware/software.
This repository is independently maintained and does not imply endorsement by
Peter Kuhar or InteliWEAR. No original branding artwork is included.

Spectre.Console and Spectre.Console.Cli are planned dependencies. Their licenses
must be recorded with other distributed third-party components when dependencies
are introduced. No Spectre package is included in this planning commit.

## First implementation preview

`src/SidePulse.Core/LedProgram.cs` adapts status animation profiles from the Python
Windows companion and the upstream LEDS.LED protocol. `DeviceTransport.cs` follows
the upstream virtual-file write contract and `PhoneBridge.cs` follows the APNS
bridge payload/channel contract. Reference: upstream revision above, files
`LEDS_FORMAT.md`, `src/sidepulse/device_writer.py` and `src/sidepulse/links.py`.
The retained upstream MIT notice applies to these adapted portions.

Spectre.Console, Spectre.Console.Cli and Spectre.Console.Ansi 0.57.2 are MIT
licensed dependencies, copyright Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi and contributors.
Their repository license is retained in `third-party/spectre.console/LICENSE.md` and included in CI distribution artifacts.
Test packages are development dependencies and are not part of the application.

## Phone linking and credential storage

`Pairing.cs` and `PhoneLink.cs` adapt the QR URI, SSE registration and APNS token
normalization contracts from upstream `src/sidepulse/links.py` at the reference
revision above. `LinkStore.cs` and CLI link management are new .NET implementations.
The upstream MIT notice is retained with these adapted protocol portions.

QRCoder 1.8.1 is MIT licensed, copyright Raffael Herrmann and Shane Krueger.
Its original license is retained in `third-party/qrcoder/LICENSE.txt`.
System.Security.Cryptography.ProtectedData 10.0.13 and the Microsoft libraries
resolved with QRCoder are MIT licensed .NET components. Windows preview artifacts
include the .NET license and third-party notices in `third-party/dotnet`.

## Platform adapters

`PlatformSecrets.cs` and `MacKeychainVault.cs` are new implementations using native
OS credential APIs/helpers and .NET AES-GCM. No third-party source was copied for
these adapters. The credential APIs do not require a cloud account or token.
