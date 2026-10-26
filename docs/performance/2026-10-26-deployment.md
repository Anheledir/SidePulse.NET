# Trimming, ReadyToRun and Native AOT investigation

Date: 2026-10-26. Tracks [#26](https://github.com/Anheledir/SidePulse.NET/issues/26).
Baseline application source: `db69bedd20f6711ac2e4bf83dabf6b090aab6a54`.
Local toolchain: .NET SDK 10.0.100/runtime 10.0.0, linux-x64, GCC, Linux 6.18.55,
Intel Xeon Platinum 8573C, three logical CPUs. Other native platforms are evaluated
by the Deployment experiments workflow; do not infer six-runtime certification
from one Linux run.

## Decision

Keep the existing untrimmed self-contained release as the supported default.
**Partial trimming plus ReadyToRun is a promising smaller/faster candidate**, but
clean analysis still exposes reflection-based JSON and CLI metadata risks. Neither
successful compilation nor a successful bare version probe is release acceptance.
Do not lower production warning policy to ship the prototype.

- [#60](https://github.com/Anheledir/SidePulse.NET/issues/60): typed JSON contracts,
  source generation and format/IPC/schema compatibility (P1).
- [#61](https://github.com/Anheledir/SidePulse.NET/issues/61): trimming/AOT-safe
  command/settings registration and third-party metadata handling (P2).

Re-evaluate promotion after these fixes, strict analysis, native credentials and
full package lifecycle acceptance. Native AOT needs platform-native toolchains;
JIT bundle cross-publishing support does not imply AOT cross-compilation support.

## Local results

| Variant | Executable | gzip executable | Full CLI / lifecycle smoke |
|---|---:|---:|---|
| Existing self-contained bundle | about 76.6 MB | about 33.2 MB | Supported baseline |
| Unrestricted trimming | 18.05 MB | not used as a candidate | Full command startup crashes |
| Native AOT | 13.73 MB | not used as a candidate | Full command startup crashes |
| Partial trimming / retained app metadata | 23.13 MB | see raw report | All seven local smokes pass with metadata retained |
| Partial trimming + ReadyToRun | 39.46 MB | 16.98 MB | All seven local smokes pass; analysis warnings remain |

MB is decimal. Gzip measurements are executable-only, not complete release archives.
Native AOT used `CppCompilerAndLinker=gcc` because this local machine has no clang;
Windows/macOS use their native toolchains in the workflow. Debug symbols are not
counted as executable bytes. Native AOT does not require the .NET runtime at launch.

Nine paired empty/reused bundle-cache launches with no simultaneous build:

| Variant | Warm version | Warm state preview | Warm Home (Unix PTY) |
|---|---:|---:|---:|
| Untrimmed reference from startup investigation | 29.15 ms | 169.04 ms | 187.99 ms |
| Partial trimming, clean measurement | 73.37 ms | 384.06 ms | 399.23 ms |
| Partial trimming + ReadyToRun | 26.93 ms | 79.79 ms | 75.63 ms |

These runs are shared-machine observations, not cold OS/antivirus measurements.
The untrimmed numbers come from the preceding startup report, not an interleaved
same-job pair; the native workflow rebuilds and measures a full baseline in the
same environment. ReadyToRun restores precompiled code after trimming; the plain
trimmed runtime does considerably more startup/JIT work here. All tested Linux
commands observed zero extracted cache bytes. No Windows Home timing is inferred
from redirected execution.

## Compatibility evidence

Unrestricted trimming and raw AOT both retain a working bare `--version`, but
`--version --no-interactive` and state preview abort during Spectre.Console.Cli
configuration: `Could not get settings type for command ... PreferencesCommand`.
Preserving application/third-party metadata repairs this first failure in the JIT
prototype. The supported `TrimMode=partial` retains unannotated application code;
`JsonSerializerIsReflectionEnabledByDefault=true` is also needed for current JSON.
An exploratory assembly-root prototype produced the same 23.13 MB executable.

The initial incremental partial build emitted no new diagnostics. A fresh combined
build surfaced the existing IL2026 JSON and IL2104 Spectre warnings; incremental
silence is **not** a strict compatibility result. The experiment runner uses rebuild
and records analysis diagnostics and diagnostic-only warning overrides explicitly.
The ReadyToRun diagnostic run recorded 64 IL2026 occurrences and one IL2104
assembly warning. Counts include compile/link phases, not unique bugs. Raw AOT
also reports IL3050 dynamic-code diagnostics. JSON is used in protected link
storage, preferences/profiles, manifests/plans, provider ownership, IPC, HTTP bridge
payloads, diagnostics and anonymous CLI output, so the fix is a shared contract
change rather than one serializer switch.

The smaller JIT/ReadyToRun prototype passed CLI/config/profile/host tests, Unix PTY
navigation/cancellation and active-host Home, animation/offline licenses,
install/PATH/conflicts/owned uninstall, updater replacement/startup-failure rollback,
four generated agent wrappers and native OpenCode plugin-to-host smoke. Local
vault import is conditional on the configured native-vault test environment;
the workflow provisions isolated Windows/macOS/Linux vault checks. Actual phone
and physical Dot/Pro behavior remain separate acceptance in #2/#3/#4.

## Reproduction

```bash
python3 scripts/deployment_experiments.py --output /tmp/spn-deployment --samples 7
```

Use a fresh output directory. The script builds `full`, `raw-trim`, `partial-strict`,
`partial`, `partial-r2r` and `aot` separately, records exact properties/build logs,
checks real command initialization and JSON/docs/licenses, and runs existing native
smokes and benchmarks only for functioning variants. Expected candidate failures
remain visible in `report.json`; a broken full baseline fails the investigation.
Diagnostic variants explicitly permit warnings to collect evidence, never to
produce normal release assets. Only reports/logs are uploaded by the workflow.
The script suppresses crash core dumps from deliberately broken prototypes.

The default publisher, six package formats, credential identities, updater and
normal warnings-as-errors settings are unchanged. Offline docs/license resources
remain part of each candidate and are verified by existing smoke tests.

Raw local evidence:

- [Partial-trim startup](2026-10-26-partial-trim.json)
- [ReadyToRun build/probes/smokes/startup](2026-10-26-partial-r2r.json)
- [Raw trim/AOT failures and sizes](2026-10-26-trim-aot-failures.json)
- [Earlier startup baseline and method](2026-10-26-startup.md)
