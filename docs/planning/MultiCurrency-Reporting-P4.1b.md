# MultiCurrency-Reporting P4.1b — FX History Fetcher

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** `MultiCurrency-Reporting-P4.1.md` (✅ shipped — store is empty without this)

## Why this exists

P4.1 built the store. Without a fetcher, the table starts empty and every
lookup returns null → reports silently fall back to no-conversion. This
pass adds the network fetcher so historical FX rates actually populate.

## Scope

In:
- `IFxRateHistoryFetcher` interface — `FetchAsync(from, to, dateRange)` returns entries
- `YahooFxRateHistoryFetcher` impl using Yahoo's v8 chart endpoint
  (`{from}{to}=X` symbol pattern: USDTWD=X, JPYTWD=X, HKDTWD=X, EURTWD=X)
- Idempotent — calling the fetcher then `UpsertRangeAsync` won't duplicate
- Tests with mocked HttpClient — verify JSON parse + symbol mapping +
  graceful HTTP failure handling

Out (later):
- Background service that auto-polls on startup / daily — manual trigger
  for this pass; UI button + auto-poll added in P4.1c.
- ECB / central bank fallback fetcher — added if Yahoo proves unreliable.

## Task checklist

- [x] **B1** — `Assetra.Core/Interfaces/IFxRateHistoryFetcher.cs` interface.
- [x] **B2** — `Assetra.Infrastructure/Fx/YahooFxRateHistoryFetcher.cs` impl.
- [x] **B3** — Unit tests with mocked HttpMessageHandler covering:
    - happy path (JSON parses, entries match input range)
    - HTTP 404 (Yahoo returns "No data found for symbol")
    - HTTP 500 (returns empty list, doesn't throw)
    - Network exception (returns empty list, doesn't throw)
    - Same-currency (USDUSD=X) short-circuits without HTTP call
- [x] **B4** — DI registration in `ServiceCollectionExtensions`.
- [x] **B5** — Build + commit + plan doc final update.

## Acceptance

- `YahooFxRateHistoryFetcher.FetchAsync("USD", "TWD", lastWeek, today)`
  returns ~7 entries with realistic rates when called against the live
  endpoint (manual smoke test, not part of CI).
- All new unit tests pass against mocked HTTP.
- Fetcher gracefully degrades (returns empty list, no exceptions) on any
  network/HTTP/JSON-parse failure so callers can safely loop over multiple
  currency pairs without one bad pair killing the whole batch.
