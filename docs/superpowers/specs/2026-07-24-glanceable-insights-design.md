# QuotaDock Glanceable Insights — Design Spec

**Date:** 2026-07-24
**Status:** In progress

Theme: bring CodexBar-class "plan around resets" glanceability to QuotaDock, in
QuotaDock's own local-first, honest-units design language.

## Goal

Turn QuotaDock from a usage display into a usage decision aid: at a glance a user
should know whether it is safe to start a long task now, which provider is most
constrained, whether a provider is actually down, and roughly what they are
spending — without merging unlike units and without broad disk or network access.

## Design principles (unchanged, reinforced)

- Honest units: quota %, credits, tokens, requests, and currency stay labeled and
  are never summed across kinds.
- Never fabricate: a failure or unknown is stale/last-good or "unavailable," never
  a zero and never an invented projection.
- Local-first: no telemetry, no cloud; every new inspection of local state is
  opt-in and disclosed.
- Provider-independent core: logic lives in `QuotaDock.Core` with unit tests; the
  native app and CLI are thin renderers over it.

## Features (CodexBar-inspired, re-expressed in QuotaDock taste)

### 1. Reset-countdown hero + pace

Add `UsagePace`, a pure calculator that, given a metric with a limit, a window
start, and a reset time, derives burn rate (used per hour), projected value at
reset, and a `PaceStatus` of `OnTrack`, `Watch`, or `Exceeds`. Pace is computed
only when inputs are sufficient; otherwise it returns `Unknown` — never a guess.

### 2. Adaptive refresh with opt-in agent awareness

Add a `RefreshMode`: `Adaptive` (new default), fixed `1m/2m/5m/15m/30m`, or
`Manual`. Adaptive tightens near reset or when pace is `Exceeds`, and backs off
when idle. A separate, explicitly opt-in `AgentAware` flag may tighten cadence
when a local coding agent is running; detection is consent-gated, bounded, and
stores only the latest activity timestamp — never command lines, paths, or
identities. All intervals still honor `Retry-After` and capped failure backoff.

### 3. Provider status / incident awareness

Add a `ProviderStatus` type (`Operational`, `Degraded`, `Outage`, `Unknown`) with
an optional short message and observed time. Status is advisory and never
overwrites last-good usage values.

### 4. Local cost/spend estimates (7/30-day), grouped by currency

Add `SpendEstimator` over stored snapshots: for providers whose snapshots expose
currency metrics, produce rolling 7-day and 30-day spend grouped by native
currency. Excludes providers without cost history; never converts currencies.
Credits `ccusage` (via CodexBar) for the local-cost idea.

### 5. Compact / merge mode

Add `CompactSelector`: pick the single most-constrained metric (highest
used-fraction, tie-broken by soonest reset) plus an ordered switcher list.

### 6. Notifications + reset delight

Extend per-metric notifications with a quiet reset acknowledgement, off by default.

### 7. `quotadock` CLI

A bundled console app printing usage, pace, and local cost as JSON/text and
managing provider settings, reusing `QuotaDock.Core` and the same stores.

### 8. Windows widgets

Expose the hero metric + countdown as a Windows widget surface later, reusing the
same Core view models.

### 9. Config portability

Document and resolve a stable settings location with legacy fallback.

## Delivery order

1. Core (test-first): `UsagePace`, `ProviderStatus`, adaptive `RefreshPolicy`,
   `SpendEstimator`, `CompactSelector`, settings additions.
2. `quotadock` CLI over Core.
3. Native widget/flyout rendering of pace, incidents, compact mode, spend view.
4. Notifications + reset delight, then Windows widgets.

## Non-goals

- No 63-provider breadth chase; keep the v1 provider set and a documented
  `IUsageConnector` contract for growth.
- No broad Full Disk Access or background filesystem scanning.
- No cross-currency or cross-unit merging in any new surface.
