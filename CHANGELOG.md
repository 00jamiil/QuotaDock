# Changelog

All notable changes to QuotaDock are recorded here.

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
