# QuotaDock

QuotaDock is a local-only Windows widget for monitoring AI usage without pretending that unlike units are interchangeable. It displays quota percentages, credits, tokens, requests, and currency with explicit used/remaining semantics, source health, timestamps, and reset countdowns.

This repository contains the `0.4.2-alpha` x64 implementation. It uses native WinUI 3/XAML on .NET 10 and Windows App SDK 2.3.1. It focuses on the four local AI coding agents that expose sign-in-based usage: Codex, Claude, Grok, and Kimi.

## Provider coverage

| Provider | Source | Metrics |
| --- | --- | --- |
| OpenAI Codex | Official local Codex app-server output | Session and weekly quota, resets, credits, month tokens when reported |
| Claude subscription | Automatic read of the local Claude Code sign-in (session usage window) | Session and weekly quota with reset times, plus month-to-date tokens/cost from the local metrics log |
| Grok subscription | Automatic read of the local Grok Build sign-in | Credits and rolling usage windows with resets |
| Kimi subscription | Automatic read of the local Kimi Code sign-in | Session and weekly quota with reset times |

Organization admin APIs, OpenAI-compatible endpoints, and other providers are outside this alpha. Grok and Kimi usage endpoints are not publicly documented; their connectors read the local sign-in and fail closed (never fabricating usage) until the live endpoints are verified.

## Privacy and security

- No account system, telemetry, cloud sync, or hosted backend.
- Provider sign-ins stay in each official CLI's own local credential store. QuotaDock reads a token in-memory for a single usage request and never persists, logs, or retransmits it.
- Non-secret settings and 30 days of normalized snapshots are stored in `%LOCALAPPDATA%\QuotaDock\quotadock.db`.
- Provider failures preserve last-good snapshots as stale values; they never become fabricated zeroes.
- Progress bars are shown only for a user-defined local soft budget and are labeled as such.
- Notifications are off until enabled per metric.

See [docs/privacy-security.md](docs/privacy-security.md) for the threat boundaries.

## Build

Requirements: Windows 10 1809+ or Windows 11, x64, and .NET 10 SDK.

```powershell
dotnet restore QuotaDock.slnx -p:Platform=x64
dotnet build src/QuotaDock.App/QuotaDock.App.csproj -c Release -p:Platform=x64
dotnet test tests/QuotaDock.Core.Tests/QuotaDock.Core.Tests.csproj -c Release
dotnet test tests/QuotaDock.Connectors.Tests/QuotaDock.Connectors.Tests.csproj -c Release
dotnet test tests/QuotaDock.Infrastructure.Tests/QuotaDock.Infrastructure.Tests.csproj -c Release
```

Build self-contained portable and unsigned MSIX artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

The current ZIP and MSIX are written to `downloads/latest`. Older downloads are kept under `downloads/archive/<version>`, while expanded publish and staging files remain in ignored `artifacts/release-work`.

The unsigned MSIX is intended for development installation or downstream signing. Trusted signing, Store/WinGet distribution, automatic updates, ARM64, and macOS/Linux are post-alpha work.

## Run native UI tests

Install the Python requirements from `e2e/requirements.txt`, build the app, and set `APP_PATH` to `QuotaDock.App.exe` before running `pytest -c e2e/pytest.ini e2e/tests`. The suite starts the app with `--e2e`, redirects all app data to a test sandbox, and uses deterministic local snapshots; it does not access provider accounts.

## Architecture

- `QuotaDock.Core`: immutable domain contracts, normalization, refresh policy, fallback semantics.
- `QuotaDock.Connectors`: local sign-in readers and usage clients for Codex, Claude, Grok, and Kimi, with fail-closed payload parsers.
- `QuotaDock.Infrastructure`: SQLite persistence, Credential Manager vault, diagnostic redaction.
- `QuotaDock.App`: native tabbed widget, details window, app-driven provider sign-in, tray service, startup and placement.
- `tests` and `e2e`: xUnit coverage plus native UI Automation tests.

## Alpha notes

Usage parsers deliberately fail closed with `format changed` when a provider's payload no longer matches an expected signature, so QuotaDock never fabricates quota. The Grok and Kimi usage endpoints are not publicly documented; their connectors read the confirmed local sign-in and degrade safely until the live payload shapes are verified and the parsers extended.

Licensed under the MIT License.
