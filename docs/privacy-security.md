# Privacy and security model

QuotaDock runs entirely on the local Windows account. Its trust boundary includes the native process, Windows Credential Manager, the local SQLite database, provider HTTPS endpoints, the installed Codex CLI, the user's default browser for Claude, and the provider-isolated Alibaba WebView2 profile.

## Data locations

| Data | Storage | Retention |
| --- | --- | --- |
| Provider API keys | Windows Credential Manager under `QuotaDock/connector-*` | Until disconnect |
| Normalized usage snapshots | `%LOCALAPPDATA%\QuotaDock\quotadock.db` | 30 days |
| Connection metadata, pins, budgets, notification and window settings | Same SQLite database | Until changed/disconnected |
| Claude browser credentials and cookies | Never accessed | Browser-owned |
| Copied Claude usage text | Never stored | In-memory parse only |
| Alibaba dashboard cookies | Alibaba-specific WebView2 directory | Until the profile is removed |
| Raw dashboard HTML/text | Never stored | In-memory parse only |

The database contains secret references, never secret values. Disconnect removes a connector credential before its metadata. Browser-import and dashboard-reader connections contain no copied browser credentials.

Custom OpenAI-compatible base URLs require HTTPS, except for loopback-only HTTP development endpoints. URLs containing embedded credentials or fragments are rejected. Aggregate usage URLs must use the same origin as the model endpoint, and authenticated clients disable automatic redirects.

## Network behavior

Official connectors call only their provider API base URL. Readers allow HTTPS top-level navigation only within the provider domain family, cancel new windows and downloads, and deny permission requests. Alibaba inference keys are never accepted or used for monitoring.

## Failure behavior

401/403 becomes `authentication required`, 429 becomes `rate limited` with `Retry-After`, recognized transient failures become `unavailable`, and unexpected payload/page signatures become `format changed`. Last-good normalized metrics remain visible with their original timestamp and stale health. A failure never creates a zero-value snapshot.

## Logs and diagnostics

Production code emits no provider payload or credential logs. The redactor covers authorization, API-key, cookie, and common secret forms for future diagnostics. The `--e2e` test mode can write an isolated XAML exception diagnostic into the test TEMP directory, which contains no provider access and is destroyed with the test sandbox.
