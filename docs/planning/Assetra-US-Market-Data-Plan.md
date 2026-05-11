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
    string Name,
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
    string ProviderName { get; }
    bool CanHandle(EquityInstrumentKey key);
    Task<MarketDataResult<EquityQuote>> GetQuoteAsync(EquityInstrumentKey key, CancellationToken ct);
    Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct);
}

public interface IEquityRouter
{
    Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct);

    Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
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
- [x] Add any later accepted technical decisions here before implementation.
- [x] Add implementation issue links when work starts.

Implementation note: accepted technical decisions were captured directly in the
corrections and phase notes in this file. No external issue tracker links were used for
this implementation pass.

### Phase 1 - Core Market Data Abstractions

- [x] Add `EquityInstrumentKey`.
- [x] Add `EquityQuote`.
- [x] Add `IEquityQuoteProvider`.
- [x] Add `ISymbolDirectory`.
- [x] Add provider error/result model.
- [x] Add batch quote contract for provider implementations.
- [x] Add exchange-aware fee/tax policy boundary.
- [x] Add compatibility mapper from `EquityQuote` to legacy `StockQuote` for existing quote-stream consumers.
- [x] Add unit tests for key normalization.
- [x] Add unit tests for US symbol normalization: `BRKB`, `BRK.B`, and `BRK$B`.

### Phase 2 - Router-Backed IStockService

- [x] Create `EquityRouter`.
- [x] Refactor `StockScheduler : IStockService` to use `EquityRouter` internally.
- [x] Wrap existing TWSE client as a provider.
- [x] Wrap existing TPEX client as a provider.
- [x] Wrap Fugle as a provider when configured.
- [x] Keep current Portfolio / Alerts quote subscription behavior working.
- [x] Keep `IStockService` as a streaming compatibility alias only.
- [x] Add regression tests that `0050/TWSE` and `00878/TWSE` still resolve correctly.

### Phase 3 - US Symbol Directory

- [x] Add downloader for `nasdaqlisted.txt`.
- [x] Add downloader for `otherlisted.txt`.
- [x] Merge both into `NasdaqSymbolDirectory`.
- [x] Persist directory cache in SQLite or assets cache.
- [x] Add weekly refresh job.
- [x] Add manual refresh command in Settings.
- [x] Ensure failures keep the last successful directory.
- [x] Add tests for `AAPL/NASDAQ`, NYSE-listed symbol, and AMEX-listed symbol.

Implementation note: Phase 3 uses an assets-cache directory at `Assets/market-data/nasdaq`.
The Settings refresh command only updates the offline symbol directory; US quote lookup
is completed through the router/provider layer in later phases. Portfolio UI integration
still remains Phase 8 work so ambiguous exchange selection and provider error states can
be surfaced deliberately.

### Phase 4 - Twelve Data Quote Provider

- [x] Add `TwelveDataClient`.
- [x] Add `TwelveDataQuoteProvider`.
- [x] Map provider responses into `EquityQuote`.
- [x] Normalize US ticker symbols.
- [x] Track quota usage.
- [x] Distinguish missing key, quota exceeded, rate limit, unsupported symbol, network failure.
- [x] Add integration-safe tests using canned JSON.
- [x] Add test-before-save flow for the Twelve Data API key.

Implementation note: Phase 4 registers Twelve Data as the US quote provider, persists its
API key in settings, classifies provider failures into `MarketDataErrorCode`, and exposes
Settings UI for test-before-save plus daily quota visibility. Later phases add quote
cache, trading-calendar gating, and base-currency valuation guardrails before the UI
fully exposes US-market flows.

### Phase 5 - Quote Cache

- [x] Add in-memory quote cache.
- [x] Make cache key provider-independent.
- [x] Support caller-provided `maxAge`.
- [x] Revalidate provider state when API key changes without changing quote cache identity.
- [x] Use TTL policy per caller.
- [x] Prevent US scheduler fetches from bypassing quota/cache gates.
- [x] Prevent US scheduler fetches from bypassing calendar gates.
- [x] Add unit tests for `0s`, `30s`, `60s`, and `120s` paths.

Implementation note: Phase 5 adds `IEquityQuoteCache`, `InMemoryEquityQuoteCache`, and
cache-aware `IEquityRouter` overloads. The cache key is only the canonical instrument
key (`symbol + exchange`), not the provider. `EquityQuoteCachePolicies` names the caller
TTL decisions: fresh/manual/scheduler `0s`, dashboard `30s`, alerts `60s`, hover preview
`120s`. Calendar gating is handled in Phase 6 because it needs exchange-session semantics.

### Phase 6 - Trading Calendar

- [x] Add `TradingDayKind`.
- [x] Add `ITradingCalendarService`.
- [x] Add weekend handling.
- [x] Add US full holiday handling.
- [x] Add US half-day handling.
- [x] Make scheduler stop or reduce refresh after half-day close.
- [x] Add tests for Black Friday, 12/24, and 7/3 behavior.
- [x] Add DST transition tests for 2026/2027 spring forward and fall back weeks.
- [x] Add Taiwan makeup-working-Saturday tests so TWSE/TPEX calendar behavior does not accidentally mirror US weekends.

Implementation note: Phase 6 adds `TradingCalendarService` with US full-session,
half-session, holiday, and weekend classification. `StockScheduler` now filters symbols
through `ITradingCalendarService.ShouldRefreshQuotes(...)` before hitting the router, so
US quotes stop after half-day close and do not consume quota on closed sessions.

### Phase 7 - FX and Portfolio Valuation

- [x] Preserve quote native currency.
- [x] Convert market value to app base currency before portfolio aggregation.
- [x] Add summary/input DTOs that distinguish native value from base-currency value.
- [x] Show native/base values where useful.
- [x] Ensure Portfolio summary does not add USD as TWD.
- [x] Ensure US holdings do not use Taiwan fee/tax estimation.
- [x] Add tests for USD quote converted to TWD.

Implementation note: Phase 7 adds native currency propagation on routed quotes,
base-currency conversion before portfolio summary aggregation, and guardrails that avoid
applying Taiwan sell-fee/tax estimates to explicit US holdings. Legacy holdings with a
blank exchange still keep the Taiwan fee path for backward compatibility. Explicit
native/base display models are now available at the row UI boundary, while the deeper
summary/input DTO split now carries native currency/value fields alongside the base
decimal values used by the summary engine.

### Phase 8 - UI Integration

- [x] Add US search result display with symbol, company name, exchange, currency.
- [x] Add missing API key CTA.
- [x] Add stale quote and provider error UI states.
- [x] Add quota display to Settings.
- [x] Add symbol directory update status to Settings.
- [x] API key field requires successful Test before Save is enabled.
- [x] Test calls a known Twelve Data quote endpoint such as `/quote?symbol=AAPL` and surfaces classified errors on fail.
- [x] Connect Add Record / Buy form to composite symbol directory.
- [x] Show exchange choice when symbol search resolves to multiple venues.
- [x] Connect Alerts to routed quote provider.
- [x] Connect Portfolio list and side panel to base-currency valuation.

Implementation note: Add Record / Buy autocomplete now reads from the composite symbol
directory, so TWSE/TPEX and Nasdaq Trader directory results appear in the same picker.
The suggestion row shows symbol, company name, exchange, and currency; selecting a row
carries exchange/name into the buy workflow so US symbols persist as `NASDAQ`/`USD`
instead of falling back to Taiwan inference.

Alerts also resolve new rules through the same composite directory. A US alert such as
`AAPL` is stored with its exchange (`NASDAQ`) and then flows through the router-backed
`StockScheduler`, using the same quota/cache/calendar controls as portfolio quotes.

Portfolio rows now expose native `Money` accessors and converted base-currency accessors.
The Positions list and side panel render the native amount as the primary value and show
an approximate base-currency value for cross-currency holdings. When Twelve Data is
selected but the API key is missing, the Positions page shows a setup notice pointing
users to Settings > Data Source and the required connection test.

Quote refresh failures now keep the last known quote visible only when the error is
retryable or quota/provider related. The row and side panel mark that value as a cached
quote and keep the provider error message in the tooltip. Missing API keys, unsupported
symbols, and closed-calendar states are not masked by stale cache, so the setup CTA and
actionable error states stay visible.

### Phase 9 - OHLC Persistent Cache

- [x] Add SQLite table/migration.
- [x] Add repository.
- [x] Add lazy gap fetch.
- [x] Keep provider as metadata only.
- [x] Refresh only recent mutable days.
- [x] Add tests proving provider is not part of the unique key.

Implementation note: Phase 9 adds `EquityOhlcCacheSqliteRepository` and wraps the
existing dynamic history provider with `CachedStockHistoryProvider`. The cache identity is
`symbol + exchange + interval + trade_date`; `source_provider` remains mutable metadata and
is overwritten on upsert instead of creating duplicate candles. History is fetched lazily
only when a chart/report asks for it. Fresh cached data is served directly, while recent
mutable daily candles can be refreshed after the configured freshness window. The current
provider interface still fetches by `ChartPeriod`, so missing-gap detection is conservative:
the wrapper fetches the requested period when edge coverage is stale or incomplete, then
persists the returned candles.

### Phase 10 - QA and Release Gate

- [x] `dotnet build` succeeds.
- [x] Existing TWSE/TPEX quote behavior still works.
- [x] Existing crypto quote behavior still works.
- [x] US symbol search works offline after directory download.
- [x] Missing key state is visible and actionable.
- [x] Quota exceeded state is visible and stops retry loops.
- [x] USD market value is converted before total asset aggregation.
- [x] Scheduler skips weekend/holiday and handles half days.
- [x] Docs and Settings help text are updated.

Verification note: `dotnet build .\Assetra.WPF\Assetra.WPF.csproj -c Release` and
`dotnet test .\Assetra.Tests\Assetra.Tests.csproj -c Release` both pass after this pass.

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
