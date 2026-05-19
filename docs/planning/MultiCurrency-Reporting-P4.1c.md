# MultiCurrency-Reporting P4.1c — FX History Auto-Refresh

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** `MultiCurrency-Reporting-P4.1.md` (✅) + `P4.1b.md` (✅)

## Why this exists

P4.1 built the store, P4.1b built the fetcher — but nothing calls the
fetcher, so the store stays empty. This pass adds the orchestrator that
walks each currency pair the user cares about and populates the store.

## Scope

In:
- `FxRateHistoryRefresher` — orchestrator service. Takes the fetcher
  + repo, exposes `RefreshAsync(baseCurrency, foreignCurrencies, daysBack)`
  that loops pairs and upserts.
- Startup hook in `AppStartupTasks` — fires a fire-and-forget refresh
  on app start (after a 5-sec delay so it doesn't race the splash + main
  window construction).
- 7-day default lookback. Larger backfills (initial population) can be
  invoked with bigger `daysBack`.
- Tests for the orchestrator with mocked fetcher + repo.

Out (later):
- Settings UI button "立即更新匯率歷史" — P4.1d (smaller pass, just XAML)
- Daily timer (current pass fetches only on app start; if user keeps app
  open across midnight, no auto-refresh) — defer until proven needed
- ECB fallback fetcher — Yahoo seems stable enough for v1

## Task checklist

- [x] **C1** — `Assetra.Application/Fx/FxRateHistoryRefresher.cs` —
  orchestrator. Reads `AppSettings.BaseCurrency` + `SupportedCurrencies`
  (or a sensible default list) to know which pairs to fetch.
- [x] **C2** — Hook into app startup. Find `AppStartupTasks` or equivalent,
  add a fire-and-forget call.
- [x] **C3** — DI registration in `ServiceCollectionExtensions`.
- [x] **C4** — Unit tests for `FxRateHistoryRefresher`:
    - Loops over all (foreign × base) pairs
    - Skips same-currency pairs
    - Continues on per-pair fetcher failure (one bad pair doesn't kill batch)
    - Calls `UpsertRangeAsync` with all collected entries
- [x] **C5** — Build + commit + plan doc final update.

## Acceptance

- On app start, after a brief delay, `fx_rate_history` is populated for
  each `(SupportedCurrency, BaseCurrency)` pair with the last 7 days of
  data (silently — no UI blocker; failures are swallowed per fetcher contract).
- If user holds USD positions and base is TWD, lookups for "USD → TWD" on
  any recent date return a sensible number, and the BalanceSheet warnings
  banner (from Reports-Loading-UX) stops appearing because the FX gap is filled.
- Tests pass.
