# Security policy

## Reporting

Please report suspected credential exposure, unsafe navigation, log redaction failures, or dependency vulnerabilities privately to the project maintainers. Do not include live API keys, cookies, OAuth tokens, passwords, or raw provider pages in a report.

## Supported version

Only the latest alpha release receives security fixes.

## Security invariants

- Secrets must never be stored in SQLite, settings, logs, exceptions shown to users, fixtures, or release artifacts.
- Dashboard content may be parsed in memory only and must be discarded immediately after normalization.
- Dashboard readers must remain opt-in and use provider-specific WebView2 data directories.
- Navigation, pop-ups, downloads, and permission prompts must fail closed.
- Disconnect must remove its Credential Manager entry before deleting connection metadata.
- Provider errors must never be represented as zero usage.

Run the test suites and `dotnet list package --vulnerable --include-transitive` before release.
