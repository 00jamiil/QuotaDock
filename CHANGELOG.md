# Changelog

All notable changes to QuotaDock are recorded here.

## 0.3.0-alpha — 2026-07-24

- Narrow QuotaDock to the four local AI coding agents that expose sign-in-based usage: Codex, Claude, Grok, and Kimi. Organization admin APIs, OpenAI-compatible endpoints, and the Alibaba dashboard reader are removed.
- Add Grok and Kimi subscription connectors that read each CLI's local sign-in and query its usage window, mirroring the Claude connector. Both fail closed (never fabricating usage) while their usage endpoints are verified.
- Redesign the widget around provider tabs: a Home tab plus one tab per provider. Home shows your pinned metrics (or everything before you pin anything), and every metric card has a pin toggle.
- Make the widget window resizable with an adaptive metric grid that reflows from one to many columns as you widen it, with a sensible minimum size.
- Remove the four-pinned-metric limit.

## 0.2.0-alpha — 2026-07-24

- Read Claude subscription usage automatically from the local Claude Code sign-in — no more copy/paste. Session and weekly (all-models and Opus) quota, remaining percentages, and reset times update on the normal refresh schedule.
- Enrich the Claude snapshot with month-to-date input/output tokens and estimated cost from Claude Code's local metrics log, when present.
- Add an "Auto-detect providers & models" action that scans for installed Codex and signed-in Claude Code and connects them in one click.
- Redesign the usage flyout into per-provider tabs (one tab per connected provider family plus a Connect tab), with rounded corners across cards, buttons, pills, and quota progress bars.
- Add OpenAI-compatible provider presets (OpenRouter, DeepSeek, Groq, Mistral, Together, Fireworks, xAI, Perplexity, Moonshot, Alibaba Model Studio Intl, Ollama, LM Studio) in the add-provider dialog; any custom endpoint still works.
- Claude access token is used only in-memory for a single read-only usage request and never written to SQLite or logs.

## 0.1.1-alpha — 2026-07-24

- Detect Codex CLI installations from PATH, the official Windows standalone location, and the current npm Windows package layout.
- Replace the embedded Claude login with a default-browser usage flow and explicit copied-text import.
- Add configurable OpenAI-compatible providers with model validation and optional aggregate usage endpoints.
- Add redirect blocking, same-origin enforcement, secret-query rejection, and expanded connector tests.
- Keep custom providers without aggregate endpoints clearly labeled as availability-only.

## 0.1.0-alpha — 2026-07-23

- Initial native WinUI 3 widget and tray application.
- OpenAI and Anthropic organization usage connectors.
- Codex personal quota reader and Alibaba Token Plan dashboard reader.
- Local SQLite history, Windows Credential Manager storage, soft budgets, and notifications.
