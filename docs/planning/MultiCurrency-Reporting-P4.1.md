# MultiCurrency-Reporting P4.1 — FX Rate History

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** `MultiCurrency-Reporting.md` P4.1
**Related:** existing `IFxRateProvider` (sources from `fx_rate` table — point-in-time, not historical)

## Why this exists

Today `IFxRateService` / `IFxRateProvider` return one rate per (from, to) —
the latest. Multi-currency aggregations need rates **as of any past date**,
otherwise:
- Position cost-basis in TWD becomes "TWD at today's rate", mixing market and
  FX moves.
- Daily snapshot's "total in TWD" line mutates retroactively as USD/TWD drifts.
- Sold position's market vs FX gain decomposition needs both buy-date and
  sell-date rates.

This pass builds the historical store. **Read-only consumers come in later
P4.x phases** (snapshot refactor, currency-switcher UI).

## Scope

In:
- New `fx_rate_history` table — `(date, base_ccy, quote_ccy, rate, source, ingested_at)` PK = (date, base_ccy, quote_ccy)
- `IFxRateHistoryService` async API: `GetRateAsync(date, from, to, ct)`
- SQLite repo impl with memory cache for hot reads
- Idempotent bulk ingest (`UpsertRangeAsync`)
- Unit tests covering: round-trip TWD↔USD↔TWD = 1.0±ε, missing-date fallback to last-available-business-day, idempotent bulk ingest

Out (deferred to later passes):
- Network fetcher (Yahoo / central bank) — added in P4.1b
- Snapshot schema change
- UI currency switcher
- P&L decomposition

## Task checklist

- [x] **F1** — Core: `Assetra.Core/Interfaces/IFxRateHistoryService.cs` +
  `Assetra.Core/Interfaces/IFxRateHistoryRepository.cs` +
  `Assetra.Core/Models/FxRateHistoryEntry.cs`.
- [x] **F2** — Infrastructure: `FxRateHistorySchemaMigrator.cs` creates
  table + indexes, registers with `SqliteSchemaHelper`.
- [x] **F3** — Infrastructure: `FxRateHistorySqliteRepository.cs` —
  `GetAsync(date, from, to)`, `GetNearestAsync(date, from, to)`,
  `UpsertRangeAsync(entries)`.
- [x] **F4** — Application: `FxRateHistoryService.cs` wraps the repo with
  a per-(from,to,date) `MemoryCache` (LRU, 5-min TTL) so repeated lookups
  during a single report render skip DB.
- [x] **F5** — DI registration in `ServiceCollectionExtensions`.
- [x] **F6** — Unit tests:
    - Round-trip TWD→USD→TWD = 1.0 ± 0.001
    - Missing-exact-date falls back to last available rate within 7 days
    - Bulk ingest is idempotent (no duplicate rows on second run)
    - Memory cache hits for repeated lookups (verified via second-call latency)
- [x] **F7** — Build + commit + plan doc final update.

## Acceptance

- `IFxRateHistoryService.GetRateAsync(new DateOnly(2025, 12, 31), "USD", "TWD")`
  returns ~31.5 if the test data has been ingested.
- Lookup on a date with no rate but with rates within ±7 days returns the
  nearest preceding rate.
- Lookup on a date with no rate AND no fallback returns null.
- All new tests pass; existing FX tests still pass.
