# Assetra US Market Data Plan

Last updated: 2026-05-09

This plan defines how Assetra should evolve from Taiwan-first stock search and quotes into a multi-market equity data layer that supports Taiwan and US equities without splitting the app into competing quote pipelines.

## Goals

- Keep the current TWSE / TPEX behavior stable.
- Add US symbol search for NASDAQ / NYSE / AMEX.
- Add US quote support with Twelve Data as the first primary provider.
- Keep ViewModels independent from provider details.
- Route Portfolio, Alerts, Dashboard, Add Record, and future features through the same market data abstraction.
- Convert USD market values through the existing FX service before contributing to base-currency portfolio totals.
- Make quota, cache, trading calendar, provider failures, and stale data visible enough that users understand what happened.

## Non-Goals

- Do not use Yahoo unofficial endpoints as a primary quote source.
- Do not add Finnhub fallback in the first implementation batch. Keep the provider interface ready, but avoid multi-provider fallback complexity until Twelve Data behavior is proven.
- Do not use Alpha Vantage as the main free provider because the free daily limit is too small for portfolio refresh workflows.
- Do not fetch large historical OHLC ranges immediately when a user creates a holding.
- Do not make ViewModels branch on TWSE / TPEX / Twelve Data / Finnhub directly.

## Provider Decision

| Provider | Role | Notes |
| --- | --- | --- |
| TWSE official | Existing Taiwan quote provider | Keep as a routed provider. |
| TPEX official | Existing Taiwan quote provider | Keep as a routed provider. |
| Fugle | Existing Taiwan quote/history provider | Keep as a routed provider when configured. |
| Twelve Data | First US quote provider | Basic tier quota must be surfaced in Settings. |
| Finnhub | Future fallback candidate | Interface-ready only in v1. |
| Nasdaq Symbol Directory | Offline US symbol directory | Use for search/resolve, not quote. |
| Yahoo Finance unofficial | Historical fallback only where already used | Do not make it the primary quote provider. |
| Alpha Vantage | Not recommended | Daily free quota is too tight for Assetra. |

## Locked Technical Decisions

### OHLC Cache Key

Provider must not be part of the unique key.

```text
unique key = instrument_key + date + interval
instrument_key = exchange + ":" + normalized_symbol
```

Provider belongs to audit/source metadata:

```text
source_provider
source_updated_at
is_adjusted
currency
```

Reason: OHLC is factual market data. If Twelve Data quota is exhausted and a future fallback provider is used, the same `AAPL / NASDAQ / 2024-01-15 / 1d` candle should not be duplicated under two providers.

### Quote Cache Key

Realtime quote memory cache also uses the instrument key, not provider:

```text
key = exchange + ":" + normalized_symbol
```

Changing an API key triggers a one-time re-validation of provider state through the Settings "Test connection" flow. The in-memory quote cache does not auto-invalidate just because a provider or API key changed; quote values are factual market data regardless of which configured key fetched them. Provider/key changes may update provider health and quota state, but they do not change the cache identity.

### Cache TTL Is Caller-Driven

Do not hard-code one global quote TTL. The caller decides freshness:

| Caller | Expected behavior | Suggested max age |
| --- | --- | ---: |
| Manual refresh | Always attempt a fresh fetch | `0s` |
| Dashboard auto redraw | Avoid quota noise | `30s` |
| Alerts evaluation | Avoid stale alert decisions, but conserve quota | `60s` |
| Hover preview / non-critical render | Avoid dense DataGrid scroll/hover API churn | `120s` |
| Scheduler 5-minute cycle | Scheduler already throttles | `0s` |

Target interface:

```csharp
Task<EquityQuote?> GetAsync(
    EquityInstrumentKey key,
    TimeSpan maxAge,
    Func<CancellationToken, Task<EquityQuote?>> fetch,
    CancellationToken ct);
```

### Trading Calendar Must Model Half Days

US markets have early-close sessions. The calendar must not be a simple open/closed boolean.

```csharp
public enum TradingDayKind
{
    FullSession,
    HalfSession,
    Holiday,
    Weekend
}
```

The scheduler must stop or sharply reduce US quote refresh after half-day early close.

### Symbol Directory Is Offline

US symbol search should not burn quote API quota. Use Nasdaq Trader symbol directory files:

```text
nasdaqlisted.txt
otherlisted.txt
```

Both files must be merged. `nasdaqlisted.txt` alone only covers NASDAQ-listed instruments; `otherlisted.txt` is needed for NYSE / NYSE Arca / BATS / AMEX.

### Symbol Normalization Rules

Internal canonical US ticker format uses dot notation.

- Uppercase all symbols.
- Trim whitespace.
- Twelve Data uses dot notation for class shares, for example `BRK.B`.
- Nasdaq Trader directory may expose class shares with a dollar suffix, for example `BRK$B`.
- Internal canonical format is dot notation, for example `BRK.B`.
- Search must match both common user input and directory/provider forms: `BRKB`, `BRK.B`, and `BRK$B` should resolve to the canonical instrument when the directory contains the listing.
- Preserve exchange in the instrument key; do not infer that a bare symbol is globally unique.

### IStockService Contract Stays, Implementation Routes

Keep the existing `IStockService` public contract so existing callers do not all need to be rewritten in one pass. Replace the implementation with a router-backed service:

```text
IStockService
  -> EquityRouterStockService
      -> EquityRouter
          -> TWSE provider
          -> TPEX provider
          -> Fugle provider
          -> Twelve Data provider
          -> Null provider
```

This avoids creating two market data centers inside the app.

`IStockService` is a transitional alias for existing callers. New market-data code should depend directly on `EquityRouter`, `IEquityQuoteProvider`, `ISymbolDirectory`, or the narrow interface that matches its responsibility. Plan to remove or rename the transitional `IStockService` wrapper in v2.1 after callers have migrated.

## Proposed Architecture

```text
Portfolio / Alerts / Dashboard / Add Record
             |
             v
        IStockService
             |
             v
   EquityRouterStockService
             |
             v
        EquityRouter
        /          \
       /            \
ISymbolDirectory   IEquityQuoteProvider[]
       |            |
       |            +-- TwseQuoteProvider
       |            +-- TpexQuoteProvider
       |            +-- FugleQuoteProvider
       |            +-- TwelveDataQuoteProvider
       |            +-- NullQuoteProvider
       |
       +-- TwseStaticDirectory
       +-- TpexStaticDirectory
       +-- NasdaqSymbolDirectory
```

## Core Types

```csharp
public sealed record EquityInstrumentKey(
    string Symbol,
    string Exchange);

public sealed record EquityQuote(
    EquityInstrumentKey Instrument,
    decimal Price,
    decimal? PreviousClose,
    decimal? Change,
    decimal? ChangePercent,
    string Currency,
    DateTimeOffset UpdatedAt,
    string SourceProvider,
    bool IsDelayed);

public interface IEquityQuoteProvider
{
    bool CanHandle(EquityInstrumentKey key);
    Task<EquityQuote?> GetQuoteAsync(EquityInstrumentKey key, CancellationToken ct);
    Task<IReadOnlyList<EquityQuote>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct);
}

public interface ISymbolDirectory
{
    IReadOnlyList<StockSearchResult> Search(string query);
    StockSearchResult? Resolve(string symbol, string? exchange = null);
}
```

## Data Storage

### US Symbol Directory Cache

Suggested table:

```text
market_symbol_directory
  symbol TEXT NOT NULL
  exchange TEXT NOT NULL
  name TEXT NOT NULL
  asset_type TEXT NOT NULL
  currency TEXT NOT NULL
  is_etf INTEGER NOT NULL
  is_active INTEGER NOT NULL
  source TEXT NOT NULL
  source_updated_at TEXT NOT NULL
  PRIMARY KEY(symbol, exchange)
```

### OHLC Cache

Suggested table:

```text
equity_ohlc_cache
  symbol TEXT NOT NULL
  exchange TEXT NOT NULL
  interval TEXT NOT NULL
  trade_date TEXT NOT NULL
  open REAL NOT NULL
  high REAL NOT NULL
  low REAL NOT NULL
  close REAL NOT NULL
  volume INTEGER NOT NULL
  currency TEXT NOT NULL
  source_provider TEXT NOT NULL
  source_updated_at TEXT NOT NULL
  is_adjusted INTEGER NOT NULL
  PRIMARY KEY(symbol, exchange, interval, trade_date)
```

### Quota Usage

Suggested table:

```text
market_data_quota_usage
  provider TEXT NOT NULL
  usage_date TEXT NOT NULL
  used_count INTEGER NOT NULL
  daily_limit INTEGER NOT NULL
  updated_at TEXT NOT NULL
  PRIMARY KEY(provider, usage_date)
```

## Settings UX

Settings should show provider state clearly:

- Quote provider: Official Taiwan / Fugle / Twelve Data.
- Twelve Data API key field.
- Test connection button.
- Save is enabled for a newly entered Twelve Data key only after a successful test.
- Test connection calls a known quote endpoint such as `/quote?symbol=AAPL` and surfaces classified provider errors on failure.
- Today's quota: `X / 800`.
- Last successful quote time.
- Last provider error.
- US symbol directory last updated time.
- Button to refresh US symbol directory.

User-facing states:

| State | UI behavior |
| --- | --- |
| Missing API key | US symbol search can work offline, but quote panels show a CTA to Settings. |
| Quota exceeded | Show "today's quota is exhausted" and stop retry loops. |
| Rate limited | Show temporary provider limit; keep cached quote if available. |
| Provider unavailable | Show stale quote marker if cache exists; otherwise show unavailable. |
| Symbol unsupported | Show unsupported state for that instrument, not a generic failure. |
| Offline directory unavailable | Keep existing cached directory; if no cache exists, show setup/retry state. |

## FX Integration

US quote values must keep native and base-currency values separate:

```text
QuotePriceNative = USD
MarketValueNative = USD
MarketValueBase = user base currency, e.g. TWD
FxRate
FxDate
```

Portfolio totals must sum only base-currency values. UI may display both:

```text
US$185.20
Approx. NT$5,982
```

## Historical Data Strategy

Use lazy backfill with persistent cache:

1. Create or import the holding without immediately fetching months of history.
2. When a chart/report needs historical data, ask the OHLC cache for missing ranges.
3. Fetch only missing dates/ranges.
4. Persist successful results.
5. Treat old trading days as immutable unless the user explicitly rebuilds history.
6. Allow the most recent trading day to refresh because provider data can settle after close.

## Implementation Corrections Before Phase 1

These corrections are required because the current Assetra codebase is Taiwan-first and several legacy names hide narrower responsibilities.

- `IStockService` is currently a streaming scheduler contract (`QuoteStream`, `Start`, `Stop`), not a general quote lookup service. Keep `StockScheduler` as the transitional `IStockService` implementation, but make the scheduler call `EquityRouter` internally. New market-data code should not add more responsibilities to `IStockService`.
- `StockQuote` is a legacy UI/scheduler payload. It has Taiwan-specific assumptions such as volume measured in board lots and no `Currency`, `SourceProvider`, or `IsDelayed`. US providers should emit `EquityQuote`; only the compatibility stream should adapt routed quotes back to `StockQuote` where existing callers still require it.
- US quote refresh must not enter the existing fast scheduler path until quota tracking, caller-driven cache TTL, and trading calendar checks are in place. The current Taiwan scheduler cadence is too aggressive for Twelve Data Basic quota.
- Portfolio totals and dashboard summaries must aggregate base-currency values only. Native quote values such as USD market value must be carried separately and converted through FX before reaching summary inputs.
- Fee and tax estimation must become exchange-aware. The existing Taiwan sell-fee/tax estimator can remain for TWSE/TPEX, but US holdings must either use a US-specific cost model or clearly display that net P/L is pre-transaction-cost until that model exists.
- Symbol lookup must be exchange-aware. Symbol-only APIs such as `GetExchange(symbol)` can remain as transitional helpers, but new flows should resolve through `ISymbolDirectory.Resolve(symbol, exchange?)` and show an explicit exchange picker when a symbol is ambiguous.
- Provider interfaces should support batch quote fetches. Router and scheduler code should group unique instrument keys and use `GetQuotesAsync` when available, instead of issuing one HTTP request per visible row.

## Phase Plan

### Phase 0 - Document and Decision Lock

- [x] Create this plan.
- [ ] Add any later accepted technical decisions here before implementation.
- [ ] Add implementation issue links when work starts.

### Phase 1 - Core Market Data Abstractions

- [ ] Add `EquityInstrumentKey`.
- [ ] Add `EquityQuote`.
- [ ] Add `IEquityQuoteProvider`.
- [ ] Add `ISymbolDirectory`.
- [ ] Add provider error/result model.
- [ ] Add batch quote contract for provider implementations.
- [ ] Add exchange-aware fee/tax policy boundary.
- [ ] Add compatibility mapper from `EquityQuote` to legacy `StockQuote` for existing quote-stream consumers.
- [ ] Add unit tests for key normalization.
- [ ] Add unit tests for US symbol normalization: `BRKB`, `BRK.B`, and `BRK$B`.

### Phase 2 - Router-Backed IStockService

- [ ] Create `EquityRouter`.
- [ ] Refactor `StockScheduler : IStockService` to use `EquityRouter` internally.
- [ ] Wrap existing TWSE client as a provider.
- [ ] Wrap existing TPEX client as a provider.
- [ ] Wrap Fugle as a provider when configured.
- [ ] Keep current Portfolio / Alerts quote subscription behavior working.
- [ ] Keep `IStockService` as a streaming compatibility alias only.
- [ ] Add regression tests that `0050/TWSE` and `00878/TWSE` still resolve correctly.

### Phase 3 - US Symbol Directory

- [ ] Add downloader for `nasdaqlisted.txt`.
- [ ] Add downloader for `otherlisted.txt`.
- [ ] Merge both into `NasdaqSymbolDirectory`.
- [ ] Persist directory cache in SQLite or assets cache.
- [ ] Add weekly refresh job.
- [ ] Add manual refresh command in Settings.
- [ ] Ensure failures keep the last successful directory.
- [ ] Add tests for `AAPL/NASDAQ`, NYSE-listed symbol, and AMEX-listed symbol.

### Phase 4 - Twelve Data Quote Provider

- [ ] Add `TwelveDataClient`.
- [ ] Add `TwelveDataQuoteProvider`.
- [ ] Map provider responses into `EquityQuote`.
- [ ] Normalize US ticker symbols.
- [ ] Track quota usage.
- [ ] Distinguish missing key, quota exceeded, rate limit, unsupported symbol, network failure.
- [ ] Add integration-safe tests using canned JSON.
- [ ] Add test-before-save flow for the Twelve Data API key.

### Phase 5 - Quote Cache

- [ ] Add in-memory quote cache.
- [ ] Make cache key provider-independent.
- [ ] Support caller-provided `maxAge`.
- [ ] Revalidate provider state when API key changes without changing quote cache identity.
- [ ] Use TTL policy per caller.
- [ ] Prevent US scheduler fetches from bypassing quota/calendar/cache gates.
- [ ] Add unit tests for `0s`, `30s`, `60s`, and `120s` paths.

### Phase 6 - Trading Calendar

- [ ] Add `TradingDayKind`.
- [ ] Add `ITradingCalendarService`.
- [ ] Add weekend handling.
- [ ] Add US full holiday handling.
- [ ] Add US half-day handling.
- [ ] Make scheduler stop or reduce refresh after half-day close.
- [ ] Add tests for Black Friday, 12/24, and 7/3 behavior.
- [ ] Add DST transition tests for 2026/2027 spring forward and fall back weeks.
- [ ] Add Taiwan makeup-working-Saturday tests so TWSE/TPEX calendar behavior does not accidentally mirror US weekends.

### Phase 7 - FX and Portfolio Valuation

- [ ] Preserve quote native currency.
- [ ] Convert market value to app base currency before portfolio aggregation.
- [ ] Add summary/input DTOs that distinguish native value from base-currency value.
- [ ] Show native/base values where useful.
- [ ] Ensure Portfolio summary does not add USD as TWD.
- [ ] Ensure US holdings do not use Taiwan fee/tax estimation.
- [ ] Add tests for USD quote converted to TWD.

### Phase 8 - UI Integration

- [ ] Add US search result display with symbol, company name, exchange, currency.
- [ ] Add missing API key CTA.
- [ ] Add stale quote and provider error UI states.
- [ ] Add quota display to Settings.
- [ ] Add symbol directory update status to Settings.
- [ ] API key field requires successful Test before Save is enabled.
- [ ] Test calls a known Twelve Data quote endpoint such as `/quote?symbol=AAPL` and surfaces classified errors on fail.
- [ ] Connect Add Record / Buy form to composite symbol directory.
- [ ] Show exchange choice when symbol search resolves to multiple venues.
- [ ] Connect Alerts to routed quote provider.
- [ ] Connect Portfolio list and side panel to base-currency valuation.

### Phase 9 - OHLC Persistent Cache

- [ ] Add SQLite table/migration.
- [ ] Add repository.
- [ ] Add lazy gap fetch.
- [ ] Keep provider as metadata only.
- [ ] Refresh only recent mutable days.
- [ ] Add tests proving provider is not part of the unique key.

### Phase 10 - QA and Release Gate

- [ ] `dotnet build` succeeds.
- [ ] Existing TWSE/TPEX quote behavior still works.
- [ ] Existing crypto quote behavior still works.
- [ ] US symbol search works offline after directory download.
- [ ] Missing key state is visible and actionable.
- [ ] Quota exceeded state is visible and stops retry loops.
- [ ] USD market value is converted before total asset aggregation.
- [ ] Scheduler skips weekend/holiday and handles half days.
- [ ] Docs and Settings help text are updated.

## Acceptance Criteria

- Searching `AAPL` returns Apple with `NASDAQ` and `USD`.
- Searching a NYSE symbol from `otherlisted.txt` returns a result.
- Searching `0050` still returns TWSE Taiwan data.
- Portfolio totals do not add USD values as TWD.
- US holdings do not display Taiwan transaction tax as part of net value or P/L.
- Alerts for US symbols do not consume quota repeatedly within the chosen TTL.
- A missing Twelve Data key never results in silent empty search/quote state.
- A quota exceeded response appears in Settings and relevant quote UI.
- Switching provider or API key revalidates provider state without changing the provider-independent quote cache identity.
- Historical OHLC cache uses `symbol + exchange + interval + date` as unique identity.
- The app keeps working if US symbol directory refresh fails but a prior cache exists.
- US symbol normalization resolves `BRKB`, `BRK.B`, and `BRK$B` consistently to the canonical internal representation.
- Trading calendar tests cover US half days, 2026/2027 DST transition weeks, and Taiwan makeup-working Saturdays.

## Open Questions

- Should US quote provider be selectable globally, or should the router choose by exchange regardless of global provider?
- Should the app allow manual exchange override when a symbol exists on multiple venues?
- Should Twelve Data quota be tracked pessimistically before request or after successful response?
- Should US historical OHLC be part of the first release, or held until quote/valuation is stable?
- Should credentials remain in app settings initially, or should this work wait for the OS credential vault abstraction?

## Implementation Notes

- Prefer additive migration: wrap current TWSE/TPEX behavior before changing callers.
- Keep provider-specific DTOs inside Infrastructure.
- Keep domain-facing quote records provider-neutral.
- Avoid throwing provider failures into UI subscriptions; convert them into explicit provider state.
- Keep API-key and quota display user-friendly. Empty results are not an acceptable error state.
