# Changelog

All notable changes to QuotaDock are recorded here.

## 0.4.2-alpha — 2026-07-26

- Restructure settings into two top-level tabs: Appearance and Models. Models hosts Connect plus nested per-provider settings.
- Add an explicit Apply button for appearance changes. Live preview stays available; Apply saves to disk and broadcasts the theme to every open window.
- Collapse/expand now toggles only the clicked card through `CollapsedMetricIds`, with a wrap layout that keeps mixed card heights tidy.
- Add hide/show toggles per metric card in Models settings, stored in `HiddenMetricIds` and respected by the widget.
- Stop appearance controls from resetting to dark when provider tabs rebuild.
- Fix title clipping in the settings header and replace hardcoded dark-only colors with theme brushes so light mode stays readable.
- Refresh theme brushes on every launch so a previously saved palette always wins over App.xaml defaults.

## 0.4.1-alpha — 2026-07-25

- Fix live theming so color, preset, and dark/light changes repaint both the widget and the settings window instantly. The shared brushes are now mutated in place instead of being replaced, which left existing bindings pointing at stale colors.
- Flush a pending appearance edit when the settings window closes, so the last change is no longer lost to the save debounce and the widget stops reverting on exit.
- Derive the on-accent text color for accent-filled buttons so their labels stay readable across custom palettes.

## 0.4.0-alpha — 2026-07-25

- Add an Appearance settings tab: pick background, text, foreground, and accent colors with live preview; choose a color preset; switch dark/light mode; and pick a window theme — Default (solid), Glassy (frosted acrylic), or Mica. Card surfaces, borders, and progress tracks are derived from the chosen colors so a custom palette stays readable, and the on-accent text color flips for contrast.
- Make metric cards collapsible into a compact quarter-height row (progress bar plus provider and usage name) through a global compact toggle that preserves the responsive multi-column layout.
- Apply theme and color changes live across the widget and settings windows without a restart.

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
