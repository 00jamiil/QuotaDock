# QuotaDock Provider Authentication and Custom Connector Design

**Date:** 2026-07-24  
**Status:** Implemented in QuotaDock 0.1.1 alpha

## Objective

Fix the personal-provider connection experience and add user-configurable OpenAI-compatible model connections without fabricating usage data or exposing browser credentials.

## User journeys

1. As a Codex user, I can connect an installed Codex CLI or receive actionable setup guidance instead of an immediate “not found” failure.
2. As a Claude subscriber, I can authenticate through my default browser and import quota information without signing in through an embedded browser.
3. As a user of an OpenAI-compatible provider, I can validate a configured model and optionally track aggregate tokens and requests when that provider exposes a compatible usage endpoint.

## Connector architecture

### Custom OpenAI-compatible provider

Add an `IUsageConnector` implementation with these inputs:

- Account label
- Base URL
- API key, optional for local providers
- Model ID
- Aggregate-usage endpoint, optional

The connector validates the configured model through the provider's OpenAI-compatible models endpoint. It accepts HTTPS endpoints and loopback HTTP endpoints only. Automatic redirects are disabled so an authorization header cannot be forwarded to a different origin.

When no aggregate-usage endpoint is configured, validation succeeds if the model exists and the connection reports that model access is available but usage tracking is not configured. The connector returns no usage metrics in this state; it does not represent unknown usage as zero.

When an aggregate-usage endpoint is configured, the initial alpha supports the OpenAI organization usage-bucket response shape. It aggregates input tokens, output tokens, and request counts across returned buckets and pagination. Unknown or malformed shapes produce `FormatChanged`, retain the last-good snapshot, and create no metrics.

Non-secret connection settings are stored in SQLite. API keys are stored only in Windows Credential Manager and are removed when validation fails or the user disconnects.

### Anthropic personal subscription

Replace the embedded Claude login flow with a default-browser-first flow:

1. Detect a supported Claude Code executable.
2. Launch Claude Code's interactive authentication, which owns its browser-based login and credential storage.
3. Read structured local quota output only when the installed CLI exposes a supported format.
4. Otherwise open Claude's Usage page in the user's default browser and offer an explicit `Import copied usage` action.

The clipboard import reads visible page text only after a user action. It parses the text in memory, saves only normalized metrics, and immediately releases the raw string. QuotaDock never reads browser cookies, passwords, OAuth tokens, local browser databases, or Claude Code credentials.

The existing isolated Claude dashboard reader is removed from the primary connection path. Alibaba retains its isolated provider-specific reader because its v1 connector depends on console usage analysis.

### OpenAI Codex personal

Replace the immediate PATH-only connection attempt with a setup flow:

1. Search PATH and known user-local installation locations for an executable Codex CLI.
2. Probe the candidate with a short, cancellable version check.
3. Allow the user to choose an executable manually.
4. If no valid CLI is available, open official installation documentation in the default browser.
5. Let Codex CLI own authentication and its default-browser sign-in; QuotaDock reads only supported usage output.

The selected executable path is non-secret local configuration. QuotaDock does not copy or store Codex authentication material.

## Native UI changes

The provider section gains three explicit actions:

- `Set up local Codex`
- `Connect Claude in browser`
- `Add OpenAI-compatible provider`

The custom-provider dialog includes account label, base URL, model ID, optional API key, and optional aggregate-usage URL. Explanatory text states that OpenAI compatibility does not standardize aggregate usage and that an omitted usage endpoint yields availability monitoring only.

Connection cards distinguish:

- Connected with usage
- Connected; usage endpoint not configured
- Authentication required
- Rate-limited
- Format changed
- CLI missing or invalid

Last-good values remain visible with their original freshness timestamps during failures.

## Scheduling and error handling

Custom aggregate endpoints use the existing five-minute official-API refresh policy. Availability-only custom connections validate on connect and manual refresh without synthesizing usage values. Existing manual-refresh cooldown, `Retry-After`, capped transient backoff, and last-good snapshot behavior remain in force.

CLI discovery and authentication launches are cancellable and time-bounded. Browser launches use constant official URLs or validated HTTPS URLs. UI error messages are actionable but exclude secrets, authorization headers, cookies, raw responses, and copied page text.

## Security constraints

- Require HTTPS except for `localhost`, `127.0.0.1`, and `[::1]`.
- Disable automatic redirects for authenticated custom-provider requests.
- Send the API key only to the configured origin.
- Store API keys in Windows Credential Manager only.
- Never persist or log browser cookies, OAuth tokens, passwords, clipboard text, or raw dashboard pages.
- Never interpret missing, failed, or unknown usage as zero.
- Delete credentials and normalized snapshots on disconnect.

## Test strategy

Follow RED/GREEN TDD for connector and parsing changes.

Unit and integration tests cover:

- HTTPS and loopback-HTTP URL validation
- Rejection of unsafe schemes and credential-bearing URLs
- Model discovery and missing-model behavior
- Optional API-key behavior for local providers
- Usage-bucket aggregation and pagination
- Empty, partial, and malformed usage payloads
- Authentication, authorization, rate-limit, server, timeout, and cancellation responses
- Redirect blocking and authorization-header containment
- Clipboard-import parsing without persistence of source text
- CLI discovery, executable probing, and missing-CLI guidance
- Credential removal after failed validation and disconnect
- Preservation of last-good snapshots

Windows UI automation covers the three connection actions, dialog accessibility, keyboard navigation, browser-import guidance, and clear missing-CLI diagnostics. Published portable artifacts are smoke-tested after packaging. Core and connectors retain at least 80% line coverage.

## Acceptance criteria

- A missing Codex CLI opens an actionable setup experience rather than only an error banner.
- Claude authentication starts through the user's default browser, never an embedded login page.
- Any valid OpenAI-compatible model endpoint can be saved after model validation.
- Compatible aggregate usage endpoints display tokens and requests; absent endpoints are labeled as availability-only.
- No connector failure produces zero usage.
- No API key, browser credential, cookie, clipboard source text, authorization header, or raw page is stored in SQLite or logs.
- Disconnect removes the provider credential and its normalized snapshots.
- All automated tests pass, connector/core coverage remains at least 80%, and the portable alpha launches successfully.

## Out of scope

- A traffic proxy that measures inference requests routed through QuotaDock
- Arbitrary JSONPath mappings for every provider-specific billing schema
- Importing cookies or login sessions from installed browsers
- Automated consumer OAuth flows not publicly supported by the provider
- Presenting local soft budgets as provider-enforced limits
