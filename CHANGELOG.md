# Changelog

All notable changes to QuotaDock are recorded here.

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
