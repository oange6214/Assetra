# US Equities Support — Implementation Plan

**Status:** Plan (not yet started)
**Target version:** v2.0 multi-asset extension
**Estimated duration:** 4–5 weeks for one engineer
**Last updated:** 2026-05-09

## Goal

Add US equities (NYSE / NASDAQ / NYSE Arca / BATS / AMEX) as a first-class supported instrument type alongside the existing TWSE / TPEX support. Users should be able to:

- Search US tickers (offline, instant) — `AAPL`, `MSFT`, `SPY`, …
- Add a US equity position with cost basis in USD
- See current quote (price, day change) refreshed by the existing scheduler
- See portfolio total in their base currency (TWD) with USD positions FX-converted via the existing Frankfurter integration
- See historical chart (lazy-loaded on demand)

## Non-goals (out of scope for this plan)

- Real-time SIP / CTA consolidated tape (paid only — free providers all give IEX subset)
- Pre-market / after-hours quote streaming
- Options chains, futures, indices
- Order routing / brokerage integration
- WebSocket streaming (paid plans across all providers)
- Multi-provider failover (Finnhub / others) — interface left extensible, not implemented

These belong to later v2.x or are explicitly forever-out-of-scope (Assetra is a tracker, not a broker).

---

## Architecture

### High-level

```
┌─ ViewModels ────────────────────────────────────────────┐
│   PortfolioVM, AlertsVM, AddRecordVM, …                 │
└────────────┬────────────────────────────────────────────┘
             │ depends on
             ▼
┌─ IStockService (renamed → IEquityRouter, see §Migration) ┐
│   Single facade for ALL equity operations.              │
│   Routes by InstrumentKey (symbol + exchange).          │
└────────────┬────────────────────────────────────────────┘
             │
   ┌─────────┴─────────────────────────────────┐
   ▼                                           ▼
┌─ ISymbolDirectory ─────────┐    ┌─ IEquityQuoteProvider ─────────┐
│ TwseStaticDirectory        │    │ TwseQuoteProvider              │
│ TpexStaticDirectory        │    │ TpexQuoteProvider              │
│ NasdaqSymbolCsvDirectory   │    │ TwelveDataQuoteProvider        │
│   (refreshed weekly,       │    │ NullQuoteProvider (tests)      │
│    fallback to last good)  │    └────────────────────────────────┘
└────────────────────────────┘                 │
                                               ▼
                                  ┌─ IQuoteCache ─────────────────┐
                                  │ TTL-by-caller in-memory       │
                                  │ + SQLite OHLC daily cache     │
                                  └───────────────────────────────┘

Cross-cutting:
- ITradingCalendarService (FullSession / HalfSession / Holiday / Weekend)
- IQuotaTracker (Twelve Data daily/minute quota)
- IFxService (existing Frankfurter — used at aggregation boundary)
```

### Key types

```csharp
// Identity
public sealed record InstrumentKey(string Symbol, string Exchange);
//   "AAPL" + "NASDAQ", "0050" + "TWSE", "00679B" + "TWSE"

public enum Market { TWSE, TPEX, NASDAQ, NYSE, NYSE_ARCA, BATS, AMEX, OTHER }

// Quote shape (immutable, currency-tagged)
public sealed record EquityQuote(
    InstrumentKey Key,
    decimal Price,
    decimal? DayChange,
    decimal? DayChangePct,
    string Currency,         // "USD" / "TWD"
    DateTimeOffset AsOf,
    string SourceProvider);  // audit only — not part of cache key

// OHLC
public sealed record OhlcBar(
    InstrumentKey Key,
    DateOnly Date,
    decimal Open, decimal High, decimal Low, decimal Close,
    long Volume,
    string Currency);
```

---

## Phase plan (4–5 weeks)

### Phase 1 — Abstractions + Trading Calendar (3–4 days)

| Item | Files |
|---|---|
| `InstrumentKey`, `Market`, `EquityQuote`, `OhlcBar` value types | `Core/Models/Equities/` |
| `ISymbolDirectory` interface | `Core/Interfaces/` |
| `IEquityQuoteProvider` interface | `Core/Interfaces/` |
| `IQuoteCache` interface (TTL-by-caller, see §Cache) | `Core/Interfaces/` |
| `ITradingCalendarService` interface + impl (US + TW holidays + half-days) | `Core/Services/` |
| `IQuotaTracker` interface + in-memory + SQLite-persisted impl | `Application/Equities/` |

**Acceptance:** all interfaces compile, calendar service unit-tested with 20+ years of US/TW holiday cases including half-days.

### Phase 2 — IStockService → EquityRouter rename (1 week, highest risk)

This is the load-bearing migration. Done in **one PR, no half-state**.

| Step | Detail |
|---|---|
| 1 | New `IEquityRouter` interface (same shape as current `IStockService` API) |
| 2 | New `EquityRouter` class implementing `IEquityRouter` AND `IStockService` (transitional) — routes by InstrumentKey to provider |
| 3 | TwseQuoteProvider + TpexQuoteProvider extracted from current `IStockService` impl |
| 4 | DI swap: register `EquityRouter` as both interfaces; existing TWSE/TPEX providers register through it |
| 5 | grep all `IStockService` consumers (~10–20 callers); confirm no behaviour change via existing tests |
| 6 | Once green, deprecate `IStockService` as alias-of-`IEquityRouter` (keep one release for transition); remove in next minor |

**Acceptance:** all 1216+ existing tests pass with zero changes; no caller signatures break.

**Why this order:** lock in the abstraction with TWSE-only first; only then add US providers. Reverse order would force re-architecting twice.

### Phase 3 — Symbol directory (3–4 days)

| Item | Detail |
|---|---|
| `NasdaqSymbolCsvDirectory` | Parses BOTH `nasdaqlisted.txt` AND `otherlisted.txt` from `ftp://ftp.nasdaqtrader.com/SymbolDirectory/` |
| Daily refresh job | Background service; downloads on app start if last download > 7 days; preserves last-good copy as fallback |
| Local cache path | `%APPDATA%\Assetra\symbols\nasdaq-{yyyyMMdd}.csv` |
| `EquityRouter.SearchAsync(query)` | Fan-out to all registered directories, merge + dedupe by `(Symbol, Exchange)` |
| Empty / fallback handling | If no directory reachable AND no cached file, search returns explicit error (not empty list) |

**Acceptance:** offline search for `AAPL`, `BRK.B`, `SPY` returns within 50ms from cached CSV; weekly refresh does not block app startup.

### Phase 4 — TwelveDataProvider + Cache + Quota (1 week)

| Item | Detail |
|---|---|
| `TwelveDataQuoteProvider` | Endpoints: `/quote` (single + batch), `/time_series` (OHLC) |
| API key resolution | Via `IAppSettingsService` (plaintext for v2.0 cut, OS-credential-store migration tracked in v1.1 plan) |
| `IQuoteCache` impl | In-memory `ConcurrentDictionary<InstrumentKey, (EquityQuote, DateTimeOffset)>`; consumer specifies `maxAge` per call |
| OHLC cache | SQLite table `ohlc_cache(symbol, exchange, date, o, h, l, c, v, currency, fetched_at, source)`; PK = `(symbol, exchange, date)` — **provider NOT in PK** |
| `IQuotaTracker` | Tracks calls/minute + calls/day per provider; persists daily total to survive restart; resets at midnight ET |
| Error classification | `QuoteFetchError` discriminated: `QuotaExceeded / InvalidApiKey / NetworkUnavailable / SymbolNotSupported / ProviderError(detail)` |

**Acceptance:** `EquityRouter.GetQuoteAsync(InstrumentKey("AAPL", "NASDAQ"))` returns within 1s when cached, hits Twelve Data on cache miss, throws typed error on quota exhaustion.

### Phase 5 — Settings UI (3–4 days)

| Item | Detail |
|---|---|
| Settings page section: "美股報價來源" | Dropdown for provider (only Twelve Data for now) |
| API key input | PasswordBox (same pattern as LLM key field) |
| "Test connection" button | Calls `/quote?symbol=AAPL`; shows green ✓ + ticker price OR red ✗ + classified error |
| Quota display | Live: `今日已用 126 / 800` (refreshes every 60s) + minute-level: `本分鐘 3 / 8` |
| Disclaimer text | "Twelve Data 免費方案使用 IEX 數據源（約 3% 美股成交量），適合個人資產追蹤，不適合交易決策" |
| 14 i18n keys (zh-TW + en-US) | Standard pattern |

**Acceptance:** new user can paste API key, click Test → see "AAPL $185.20"; saves only on Test success.

### Phase 6 — FX aggregation (3–4 days)

| Item | Detail |
|---|---|
| `Money` value already supports currency tag | Reuse — no schema change |
| `EquityQuote.Currency` flows through to position valuation | "原幣 USD 5,000 / 估值 TWD 162,000 (USD/TWD 32.40 @ 2026-05-09)" |
| Portfolio total aggregation | Already uses `IFxService` (Frankfurter); extend to handle multi-currency positions consistently |
| UI display | Position card shows USD price prominently; TWD estimate in smaller text below; FX rate + date as tooltip |
| Stale FX warning | If Frankfurter rate > 24h old, badge "FX 資料過期" on USD positions |

**Acceptance:** add 100 shares of AAPL @ $150 cost basis; portfolio total in TWD reflects current FX-converted value; tooltip shows the rate used.

### Phase 7 — Integration (3–4 days)

| Item | Detail |
|---|---|
| Add Record dialog | Symbol search now spans TWSE + TPEX + NASDAQ + NYSE/etc. via Router |
| Portfolio refresh scheduler | Calls `EquityRouter.GetQuoteAsync(...)` for each unique InstrumentKey; respects `ITradingCalendarService` per-market hours |
| Manual refresh button | Bypasses cache (`maxAge=TimeSpan.Zero`); shows quota cost preview before |
| Alerts evaluation | Reads quotes via Router with `maxAge=60s` |
| Position chart (lazy) | When user opens chart for a US symbol, fetches OHLC range via Router; aggressive permanent cache |

**Acceptance:** end-to-end: add AAPL position → see live quote in dashboard → set price alert → trigger fires correctly → portfolio total updates after FX refresh.

---

## Cache design contract

```csharp
public interface IQuoteCache
{
    /// <summary>
    /// Returns cached quote if its age ≤ <paramref name="maxAge"/>.
    /// Pass <c>TimeSpan.Zero</c> to force-bypass (manual refresh).
    /// Returns <c>null</c> on cache miss — caller must fetch.
    /// </summary>
    EquityQuote? TryGet(InstrumentKey key, TimeSpan maxAge);

    void Put(EquityQuote quote);
}
```

### TTL by caller (no global default)

| Caller | maxAge | Rationale |
|---|---|---|
| Manual refresh button | `Zero` | User explicitly asked for fresh |
| Scheduler tick (5-min) | `Zero` | Schedule itself throttles; cache provides no value |
| Dashboard re-render | `30s` | Avoid storms during navigation |
| Alerts evaluation | `60s` | Slight staleness OK; alerts not high-frequency |
| Single-row hover preview | `120s` | Lowest priority |

### OHLC cache (SQLite, permanent)

```sql
CREATE TABLE ohlc_cache (
    symbol     TEXT NOT NULL,
    exchange   TEXT NOT NULL,
    date       TEXT NOT NULL,    -- ISO yyyy-MM-dd
    open       REAL NOT NULL,
    high       REAL NOT NULL,
    low        REAL NOT NULL,
    close      REAL NOT NULL,
    volume     INTEGER,
    currency   TEXT NOT NULL,
    fetched_at TEXT NOT NULL,    -- audit
    source     TEXT NOT NULL,    -- audit only
    PRIMARY KEY (symbol, exchange, date)
);
```

**Provider NOT in PK** — historical data is provider-agnostic. Switching to Finnhub later doesn't invalidate cache.

**Refresh rule:** if `date == today` AND market is currently open AND `fetched_at < 1h ago` → refresh; otherwise reuse forever.

---

## Trading calendar contract

```csharp
public interface ITradingCalendarService
{
    TradingDayKind ClassifyDay(Market market, DateOnly date);
    (TimeOnly Open, TimeOnly Close)? GetSession(Market market, DateOnly date);
    bool IsCurrentlyTradingHours(Market market, DateTimeOffset now);
}

public enum TradingDayKind { FullSession, HalfSession, Holiday, Weekend }
```

### US half-sessions to honour

- Day after Thanksgiving (Black Friday) — close 1pm ET
- Christmas Eve (12/24) — close 1pm ET (when on weekday)
- Day before Independence Day (7/3) — close 1pm ET (when on weekday)

### TW market

- Holidays from official MOI (內政部) calendar
- Make-up Saturdays (補班) → FullSession
- 開盤 09:00, 收盤 13:30 TPE

### Implementation

Static lookup table per year (committed to repo) with annual update task. No runtime download — calendar is small (~20 entries/year/market).

---

## Migration / coexistence with TWSE

### Existing TWSE callers

After Phase 2 rename, all of these go through the Router with zero behavioural change:
- `PortfolioViewModel.LoadQuotesAsync`
- `AddRecordViewModel.SearchSymbolsAsync`
- `AlertsService.EvaluateRulesAsync`
- All scheduler tick handlers
- ~10–20 other consumers

### Schema migration

**None required.** Existing `Trade` table stores `Symbol` + `Exchange` columns; US tickers fit the same shape. New rows will simply have `Exchange="NASDAQ"` etc.

### Existing settings

`AppSettings` gets new optional fields:
- `TwelveDataApiKey` (string, default empty)
- `LastSymbolDirectoryRefreshUtc` (DateTime?)

Both default-safe — old config files load without modification.

### User-facing migration

Zero. Existing TWSE positions continue working; user sees no change until they explicitly add a US ticker.

---

## API key UX flow

```
First-time US equity attempt
  ├─ has API key? ── no ──► Show CTA banner in search:
  │                          "請至 Settings → 美股報價來源 設定 Twelve Data API key"
  │                          [前往設定] button
  │
  └─ yes ──► attempt fetch
              ├─ success ──► display quote
              ├─ InvalidApiKey ──► snackbar:
              │                     "API key 無效，請至 Settings 重新驗證"
              ├─ QuotaExceeded ──► snackbar:
              │                     "今日已用完 800/800 quota，明日重置"
              ├─ NetworkUnavailable ──► snackbar:
              │                          "網路無法連線，將使用快取（如有）"
              └─ SymbolNotSupported ──► inline:
                                         "Twelve Data 無此標的（{symbol}）"
```

### Settings test-connection flow

```
[使用者輸入 key] → [Test 按鈕] → 打 GET /quote?symbol=AAPL → 
  ├─ 200 + 有 price ──► ✓ 綠勾 + "AAPL $185.20，連線成功" → 啟用 [儲存] 按鈕
  └─ 任何失敗   ──► ✗ 紅叉 + 分類錯誤訊息 → [儲存] 按鈕保持 disabled
```

Key never persists until Test passes.

---

## Quota tracking design

```csharp
public interface IQuotaTracker
{
    QuotaSnapshot GetSnapshot(string providerId);
    void RecordCall(string providerId, int credits = 1);
    bool CanMakeCall(string providerId, int credits = 1);
}

public sealed record QuotaSnapshot(
    int CreditsUsedToday,
    int CreditsLimitDaily,
    int CreditsUsedThisMinute,
    int CreditsLimitMinute,
    DateTimeOffset NextDailyResetUtc);
```

### Persistence

```sql
CREATE TABLE quota_usage (
    provider_id TEXT NOT NULL,
    day_utc     TEXT NOT NULL,    -- yyyy-MM-dd
    credits     INTEGER NOT NULL,
    PRIMARY KEY (provider_id, day_utc)
);
```

Survives restart. Old rows (> 30 days) auto-pruned on app startup.

### Reset times

- Twelve Data: midnight UTC (per their docs)
- Frankfurter: no quota
- TWSE / TPEX: no formal quota (informal rate limit)

---

## Risks & open decisions

| # | Risk | Mitigation |
|---|---|---|
| 1 | Twelve Data free tier could disappear / require paid | Provider abstraction means swap is local; document Finnhub as documented fallback |
| 2 | Nasdaq FTP unreachable on app startup | Last-good cached file fallback; weekly refresh, not blocking |
| 3 | OHLC cache grows unbounded for dropped positions | Vacuum task: delete rows for `(symbol, exchange)` pairs not in any current position, monthly |
| 4 | User confusion over "real-time" expectations | Explicit disclaimer in Settings + product wording rule (`報價更新` not `即時`) |
| 5 | FX rate staleness silently inflates portfolio | "FX 資料過期" badge after 24h; force refresh on any USD position display |
| 6 | EquityRouter rename breaks 3rd-party hook code | None known; we're single-tenant |
| 7 | Half-session schedule edge cases (DST transitions) | Use `DateTimeOffset` everywhere; test cases cover 2026 / 2027 / 2028 spring + fall transitions |
| 8 | Symbol directory has 8000+ entries — search performance | In-memory `Dictionary<string, SymbolEntry>` + prefix index; <50ms acceptable |

### Open decisions still to make

1. **Provider abstraction granularity** — single `IEquityQuoteProvider` for all markets, OR separate interface per market type (`IUsEquityProvider`, `ITwEquityProvider`)?
   → **Default: single interface**; markets distinguished by InstrumentKey routing. Revisit if API shapes diverge significantly.
2. **Crypto support** — explicitly out of this plan. Twelve Data has crypto endpoints; if added later, becomes a 4th `Market` enum value with its own scheduler rules (24/7, no calendar).
3. **Position currency override** — should user be able to mark an AAPL position as TWD-denominated (e.g. some Taiwan brokers offer TWD-quoted US ETFs)? Default: **no, use exchange's native currency**. Revisit if requested.

---

## Acceptance criteria for v2.0 ship

- [ ] All Phase 2 (rename) — zero existing-test regressions
- [ ] Search `AAPL` from offline directory returns < 50ms
- [ ] Add AAPL position with cost basis $150 × 100 shares
- [ ] Dashboard shows live USD quote (refreshed every 5 min during US market hours)
- [ ] Portfolio total includes AAPL value FX-converted to TWD
- [ ] Stale FX (> 24h) shows badge
- [ ] Settings test-connection validates API key before saving
- [ ] Quota display reflects actual usage; survives restart
- [ ] Manual refresh bypasses cache
- [ ] Scheduler skips US weekends + holidays + half-session early-close
- [ ] Test coverage: ≥ 90% on `EquityRouter`, `TwelveDataQuoteProvider`, `ITradingCalendarService`, `IQuotaTracker`
- [ ] Documentation: user guide section "新增美股", privacy disclosure update (data sent to Twelve Data)

---

## Effort breakdown

| Phase | Days | Notes |
|---|---|---|
| 1 — Abstractions + Calendar | 3–4 | Including holiday/half-session test data |
| 2 — IStockService → Router rename | 5–7 | Highest risk; do first to lock abstraction |
| 3 — Symbol directory | 3–4 | Two CSV files + weekly refresh + fallback |
| 4 — TwelveDataProvider + Cache + Quota | 5–7 | Including error classification + tests |
| 5 — Settings UI | 3–4 | 14 i18n keys + Test-connection |
| 6 — FX aggregation | 3–4 | Mostly UI work; backend already supports Money |
| 7 — Integration | 3–4 | End-to-end wiring + acceptance tests |
| Buffer (bug bash, docs) | 3–5 | |
| **Total** | **28–39 days ≈ 4–5 weeks** | One engineer |

---

## What this unlocks (future work, not in this plan)

- Crypto support — same architecture, new `IEquityQuoteProvider` impl + `Market.CRYPTO` enum
- Bond / ETF metadata enrichment via Twelve Data `/profile` endpoint
- Pre-market / after-hours quotes (paid plan upgrade)
- Multi-provider failover (Finnhub as Twelve Data fallback)
- Real-time WebSocket streaming (paid plan upgrade)

---

## Migration to inventory

After this plan is complete, update `Remaining-Work-Inventory.md`:
- Move "多資產擴張" v2.0 entry from "未做" to "✅ 已交付（US equities phase 1）"
- Add new follow-up entries: crypto / bonds / pre-market for v2.1+
