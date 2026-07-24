# QuotaDock

QuotaDock is a local-only Windows widget for monitoring AI usage without pretending that unlike units are interchangeable. It displays quota percentages, credits, tokens, requests, and currency with explicit used/remaining semantics, source health, timestamps, and reset countdowns.

This repository contains the `0.1.1-alpha` x64 implementation. It uses native WinUI 3/XAML on .NET 10 and Windows App SDK 2.3.1. WebView2 appears only in the opt-in, provider-isolated Alibaba dashboard reader.

## Provider coverage

| Provider | Source | Metrics |
| --- | --- | --- |
| OpenAI Codex | Official local Codex app-server output | Session and weekly quota, resets, credits, month tokens when reported |
| OpenAI organization | Usage and Costs Admin APIs | Month-to-date input/output tokens, requests, spend, project breakdowns |
| OpenAI-compatible provider | `/v1/models` plus optional same-origin aggregate endpoint | Model availability; optional month-to-date input/output tokens and requests |
| Claude subscription | Automatic read of the local Claude Code sign-in (session usage window) | Session and weekly quota with reset times, plus month-to-date tokens/cost from the local metrics log |
| Anthropic organization | Usage and Cost Admin APIs | Month-to-date tokens/spend with workspace and model breakdowns |
| Alibaba Token Plan International | Isolated Model Studio console reader | Team-plan credit quota, used/remaining, reset, identity, model breakdowns |

General ChatGPT chat/image/voice limits, Alibaba inference-key monitoring, pay-as-you-go Model Studio, Coding Plan, and other providers are outside this alpha.

## Privacy and security

- No account system, telemetry, cloud sync, or hosted backend.
- API keys are stored only in Windows Credential Manager.
- Non-secret settings and 30 days of normalized snapshots are stored in `%LOCALAPPDATA%\QuotaDock\quotadock.db`.
- Claude authentication remains in the user's default browser. QuotaDock imports copied visible text only after an explicit click, stores only normalized metrics, and never reads browser cookies or credentials.
- Alibaba dashboard cookies stay inside `%LOCALAPPDATA%\QuotaDock\WebView2\alibaba`.
- The Alibaba dashboard reader enforces HTTPS provider-domain allowlists, blocks pop-ups and downloads, denies permission prompts, and persists neither raw HTML nor visible page text.
- Custom endpoints require HTTPS except for loopback development servers. Authenticated requests do not follow redirects, and aggregate usage URLs must share the base URL's origin.
- Provider failures preserve last-good snapshots as stale values; they never become fabricated zeroes.
- API progress bars are shown only for a user-defined local soft budget and are labeled as such.
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
- `QuotaDock.Connectors`: official APIs, custom OpenAI-compatible endpoints, local Codex discovery/reader, and fail-closed page parsers.
- `QuotaDock.Infrastructure`: SQLite persistence, Credential Manager vault, diagnostic redaction.
- `QuotaDock.App`: native widget, details window, default-browser Claude import, Alibaba reader, tray service, startup and placement.
- `tests` and `e2e`: xUnit coverage plus native UI Automation tests.

## Alpha notes

Visible-page parsers deliberately fail closed with `format changed` when provider pages no longer match expected signatures. Sanitized parser fixtures can be updated without weakening cookie or page-content isolation. OpenAI and Anthropic organization keys must be Admin keys; ordinary project/API keys are insufficient for organization reports. OpenAI compatibility standardizes model discovery, not aggregate billing, so a custom provider without a compatible aggregate endpoint is labeled availability-only.

Licensed under the MIT License.
