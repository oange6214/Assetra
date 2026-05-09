# Deferred Roadmap — items NOT in v1.0 GA

**Status:** Living document
**Last updated:** 2026-05-09 (v0.28.0 release readiness sweep)

Curated list of work items that are real and prioritised but consciously **out of scope for v1.0 GA**. Each entry has a target version and an effort estimate so future sprint planning can pull off the top.

---

## v1.1 (point release after GA)

| Item | Effort | Why |
|---|---|---|
| **LlmApiKey OS credential store migration** | 4–6h | Today plaintext in `AppSettings.json`. Move to Windows Credential Manager + macOS Keychain abstraction (when MAUI port lands). Backward-compat path: read both, write only to credential store. |
| **AI insight de-dup persistence** | 2–3h | Today the dedup window is process-lifetime; restart re-spams. Persist `last_shown_at` per insight key in SQLite. |
| **AI conversation search across persisted history** | 3–4h | Today only Markdown export covers historical lookback. Add `WHERE user_text LIKE @q OR assistant_text LIKE @q` query + result list page. |
| **Audit log restore-with-reload** | 2h | Today restore inserts new trade but doesn't trigger Portfolio reload to pick up the row. Hook into `TransactionCompleted` event. |
| **Sync conflict UI improvements** | 1–2 weeks | LWW resolver works but the "manual conflict drain" page lacks side-by-side diff. |
| **i18n full audit** | 3–4h | Past 100+ commits added strings; some are still hardcoded. Catch with a Roslyn analyzer or grep sweep. |

## v1.2 (next minor)

| Item | Effort | Why |
|---|---|---|
| **PortfolioViewModel + Reload.cs split** (B2/B3) | 10–14h | Largest remaining god-object; deferred until Confirm.cs split was proven (v0.27). |
| **AI Phase 4 — action automation** | 2–3 weeks | Let LLM propose-and-execute (e.g. "record this monthly subscription"). Needs confirmation-flow design + security review. |
| **AI tool-calling proper protocol** (Phase 3.7) | 8–12h | Today Phase 3.5 invokes ALL tools and splices into prompt; switch to OpenAI-style function calling so the LLM picks per-query. |
| **AI auto monthly summary email/push** | 1 week | Phase 2 hosted service generates insights; pipe through Smtp / FCM. |
| **Sync Argon2id upgrade** | 1 week | Replace PBKDF2 KDF with Argon2id; one-time migration path for existing devices. |

## v2.0 (major, may need separate codebase or platform)

| Item | Effort | Why |
|---|---|---|
| **PWA frontend** | 3–4 weeks | Backend REST extraction + Svelte/React SPA. Shared SQLite via local-first sync. |
| **Mobile (iOS / Android)** | 4–6 weeks | MAUI or native; reuse Core/Application projects. |
| **Push notifications** | 2 weeks | APNs + FCM cross-platform abstraction. |
| **Multi-asset extensions** | 4–6 weeks | Crypto live price (CoinGecko), private funds, options chains. |

## Always-on quality work (no version target)

| Item | Effort |
|---|---|
| **Performance profiling** — BenchmarkDotNet + 10k+ trade fixture | 1 week |
| **Test coverage measurement** — coverlet → codecov, set 80% gate | 3–5 days |
| **WCAG 2.1 accessibility audit** | 1 week |
| **Reports low-resolution layout** — 1280×720 pass | 2–3h |
| **Tx commit/event sourcing audit** — extend audit log to all state changes (today only deletion) | 8–12h |

---

## How to use this doc

When pulling work for the next sprint:

1. Find the version bucket that matches release date.
2. Pick by total effort (most teams pull 80% of nominal capacity).
3. Move the item into `Implementation-Roadmap.md` for that version with full plan-doc treatment.
4. Delete from this doc when shipped (this doc tracks only deferred items).

If a v1.x bucket grows past ~2 weeks of effort, consider promoting items to v2.0 instead of bloating the point release.
