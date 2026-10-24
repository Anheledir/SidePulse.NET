# ADR 0003: Windows, macOS and Linux preview support

Status: accepted, 2026-10-23, following the explicit request for multiple platforms.

The application targets Windows, macOS and Linux with shared portable logic.
Runtime-specific credential adapters use DPAPI, native Keychain SecItem APIs and
Secret Service respectively. Unix link documents use authenticated AES-256-GCM
with per-configuration-path keys in the native vault. No plaintext fallback is
provided. Windows retains its existing path and format.

Copilot hook files include PowerShell and Bash commands. Current-user .NET named
pipes provide local IPC on all three platforms. Platform CI must exercise native
credentials, hook forwarding and simulated device writes, and package x64/ARM64
archives with execution checks for each runner's native architecture.

This supersedes the initial Windows-only delivery priority. It does not promise
parity for upstream OS-specific helpers, raw USB, firmware, installers or services.
