# Generated JSON contracts

Date: 2026-10-26. Delivery: #60; CLI dependency follow-up: #61.

## Decision

Use explicit records and System.Text.Json generated metadata for application files,
transport envelopes, IPC and CLI JSON. The executable disables reflection-based
JSON serialization by default. Unknown contracts fail explicitly; they do not fall
back to runtime reflection. Core services remain independent of terminal output
and receive their existing caller-owned HTTP clients/credential protectors.

`CoreJsonContext` registers persisted core records and the phone bridge envelope.
`CliJsonContext` registers public output and helper-plan records. The separate
`IpcJsonContext` keeps the host's numeric wire enums. Runtime option converters use
the generic enum converter, not the dynamic converter factory.

## Compatibility

- Keep existing property names/order, casing, null fields, enum representations
  and constructor defaults. Lowercase CLI reply properties are deliberate wire
  names; existing public model properties retain their PascalCase names.
- Preferences, profiles, encrypted link plaintext and ownership records retain
  their current schema. Link/ownership record implementation types are internal;
  encryption/protection and credential identities are unchanged.
- Package/install/update records retain case-insensitive reads, indentation and
  format-1/format-2 handling. Startup/helper/rollback checks remain active.
- Host messages retain numeric enums and ProtocolVersion 1. CLI output retains
  named enums. Old unversioned event defaults and numeric host responses still read.
- The bridge retains `aps.content-available`, `leds` and `data.sidepulse_event_id`;
  no alert field or push token is added to JSON output. Token-bearing PhoneLink
  objects are not registered as public CLI output contracts.
- Serializer contexts receive copies of caller options: a context encapsulates
  its options, so sharing an options instance between Core and CLI contexts would
  break uninstall/helper serialization. Copies also preserve caller mutability
  without retaining a stale cached snapshot.

## Evidence and remaining work

JsonContractTests compares generated output bytes with the prior anonymous/file
shapes and fixed legacy fixtures; it covers defaults, enum differences, protected
link payload shape, caller-owned options and unknown/secret output rejection.
The normal CLI and lifecycle smoke run with reflection JSON disabled.

```bash
python3 scripts/json_compatibility.py /path/to/baseline/spn /path/to/candidate/spn
```

This compares 22 read-only public JSON/error responses. Only executable-directory
and derived receipt-key differences are normalized. The isolated application config
must remain absent. Run against a stopped host; a live externally changing session
is not deterministic compatibility evidence. JSON compatibility CI publishes both
revisions with the same SDK on native Windows/Linux/macOS and runs this comparison.

Application JSON IL2026 diagnostics are removed, including the generic
`JsonArray.Add<JsonObject>` overload (use the concrete JsonNode overload instead).
Local strict partial-trim/ReadyToRun now reaches the third-party
Spectre.Console.Cli IL2104 warning; AOT reports that dependency's IL3050 rather than
our JSON APIs. #61 owns the remaining CLI metadata/dynamic-code compatibility.
Publishing experiments no longer re-enable reflection JSON for smaller variants.
Default release trimming/AOT flags stay unchanged until their full acceptance.
