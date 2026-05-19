# Multi-Currency Reporting

**Status:** Planning / not started
**Last updated:** 2026-05-13
**Owners:** unassigned
**Depends on:** `MultiCurrency-Trade-Refactor.md` P1–P3 (✅ landed) — Trade rows now carry `InstrumentCurrency` + `FxRate`; the data is in place but no report consumes it yet.

## Why this exists

After P1–P3, every new (and most existing) `Trade` row records the standalone facts of a transaction in a currency-aware way:

- `Price` is in `InstrumentCurrency`
- `CashAmount` is in the cash-account's currency
- `FxRate` connects the two

But every **aggregation** in the app — position cost basis, portfolio market value, daily snapshot, return %, benchmark TWR, allocation pie, financial overview KPI — still **assumes a single currency**. The moment a user has both 2330.TW (TWD) and AAPL.US (USD) positions, the existing report code multiplies USD prices by TWD reference rates inconsistently (or simply ignores the FX axis), producing numbers that are mathematically nonsense.

This doc lays out the reporting / analytics refactor required to turn the per-trade currency facts into correct, switchable, multi-currency views.

## Core principle: explicit base currency for every aggregation

There is no globally correct way to display a multi-currency portfolio. The same position can be shown in:

- **Instrument basis** (USD for AAPL, TWD for 2330) — pure market return, no FX noise
- **Base basis** (everything in TWD; or everything in USD) — total wealth in user's currency of record
- **Mixed** — leave each position in its native currency, only convert when summing across positions

Every report screen must declare which basis it uses, ideally with a switcher. Hidden defaults invite mis-reading.

## Required infrastructure

### 1. Historical FX rate store

Right now `IFxRateService` returns a live rate. Multi-currency aggregations need rates **as of any past date** — otherwise:

- Returning historical position cost in TWD becomes "TWD market value as of today's rate", which mixes market-move and FX-move.
- Daily snapshot's "total in TWD" line will mutate retroactively as the USD/TWD rate drifts.
- Realized P&L on a sold USD position needs the buy-date rate AND the sell-date rate to separate market return from FX return.

**Proposed:** new `fx_rate_history` table — `(date, base_ccy, quote_ccy, rate, source, ingested_at)` with daily granularity, sourced from Yahoo Finance or central bank API. A new `IFxRateHistoryService.GetRateAsync(date, fromCcy, toCcy)` reads from this with a memory cache.

### 2. Currency-tagged money type

Already exists: `Assetra.Core.Models.Money(decimal Amount, string Currency)` and `Assetra.Core.Models.Currency`. Use these end-to-end in report DTOs rather than bare `decimal` so the compiler enforces what the prose just said.

Refactor candidates (each is one PR):
- `PortfolioDailySnapshot.MarketValue: decimal` → either remain TWD-base and add `currency_buckets` JSON column, or split into separate snapshot rows per currency (cleaner but more rows)
- Reports DTOs (`KpiSummary`, `AllocationSlice`, `ReturnSeries`) — change `decimal` to `Money` everywhere they cross a currency boundary

### 3. Snapshot capture strategy

Two viable approaches:

**(a) Per-currency snapshot rows.** `portfolio_daily_snapshot` gains a `currency` column; one row per (date, currency). Aggregations sum across rows after FX-converting to the chosen base. **Pro:** historical data is FX-rate independent; user switching base recomputes from raw. **Con:** all existing snapshot rows need backfill or assume "TWD" default. Code that iterates snapshots needs to group-by currency.

**(b) Capture at native + base.** Each snapshot stores `market_value_native` + `market_value_base_twd` + `fx_rate_used`. **Pro:** simpler queries. **Con:** TWD column becomes stale if the user later wants USD base; needs daily re-stamping if base changes.

Recommendation: **(a)** — closer to the per-trade refactor philosophy of recording facts, deriving views.

## Phase plan

### P4.1 — FX rate history infrastructure

**Files:**
- `Assetra.Core/Interfaces/IFxRateHistoryService.cs` — async API: `Task<decimal?> GetRateAsync(DateOnly date, string from, string to, CancellationToken ct = default)`
- `Assetra.Infrastructure/Persistence/FxRateHistorySchemaMigrator.cs` — new table + indexes
- `Assetra.Infrastructure/Persistence/FxRateHistorySqliteRepository.cs` — implements the interface
- `Assetra.Infrastructure/Fx/FxRateHistoryYahooFetcher.cs` — background pull (daily) from Yahoo Finance USDTWD=X / JPYTWD=X / HKDTWD=X
- `Assetra.Tests/Infrastructure/Persistence/FxRateHistorySqliteRepositoryTests.cs`

**Acceptance:**
- `GetRateAsync(2025-12-31, "USD", "TWD")` returns the closing rate of that date (or last business day before)
- Fetcher runs idempotently — re-running doesn't duplicate rows
- Round-trip TWD→USD→TWD on the same date is `1.0` ± rounding

### P4.2 — Position aggregation refactor

**Goal:** `PositionQueryService` returns per-position values in their instrument currency, plus a separately tagged "base currency total" row.

**Files:**
- `Assetra.Application/Portfolio/Services/PositionQueryService.cs` — return type changes to use `Money`
- `Assetra.Core/Dtos/PortfolioSummaryDtos.cs` — `PositionSummary.Cost`, `MarketValue`, `UnrealizedPnl` become `Money`
- All UI bindings on positions DataGrid — pick up `.Amount` for numeric column, `.Currency` for tag column
- `AllocationView.xaml` — new "by currency" group toggle alongside "by asset class"

**Acceptance:**
- Positions table shows `成本 100 USD` next to AAPL row, `成本 50,000 TWD` next to 2330 row
- Adding a row for "Total（TWD）" sums via FX conversion using today's rate
- Color-code per currency for quick visual scan

### P4.3 — Daily snapshot per-currency split

**Goal:** Build the foundation for FX-stable historical comparisons.

**Files:**
- `Assetra.Infrastructure/Persistence/PortfolioSnapshotSchemaMigrator.cs` — add `currency TEXT NOT NULL DEFAULT 'TWD'` column; PK becomes (date, currency)
- Backfill: existing rows default to TWD, no historical USD data — that's fine, USD positions started after this refactor
- `Assetra.Infrastructure/Persistence/PortfolioBackfillService.cs` — per-trade snapshots now break out by `InstrumentCurrency`
- `Assetra.Application/Portfolio/Services/PortfolioHistoryQueryService.cs` — new `GetSnapshotsAsync(baseCurrency)` overload that joins with `fx_rate_history` to produce a base-currency time series

**Acceptance:**
- Trends page can show "TWD basis" / "USD basis" toggle that re-projects via historical FX
- Switching basis is fast (< 1s for 365 days × 5 currencies)
- Old TWD-only users see no visible change (everything stays TWD)

### P4.4 — Reports / Trends currency switcher UI

**Goal:** Wire P4.1-P4.3 into the existing reporting screens.

**Files:**
- `Assetra.WPF/Features/Trends/TrendsView.xaml` — new "計價幣別" dropdown next to period chips, options derived from `Settings.SupportedCurrencies`
- `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs` — `SelectedBaseCurrency` ObservableProperty triggers `RefreshChartAsync`
- `Assetra.WPF/Features/FinancialOverview/FinancialOverviewView.xaml` — KPI bar can pick base
- Benchmark row already picks instrument based on symbol prefix (^TWII / ^GSPC) — auto-switch to match base

**Acceptance:**
- User with mixed USD + TWD portfolio sees Trends chart cleanly in either basis
- TWR + benchmark all align on chosen base
- Switching back to TWD reverts to today's view

### P4.5 — Realized P&L breakdown (market vs FX)

**Goal:** When user sells USD-priced AAPL into TWD account, separate "marketgain" from "FX gain" in the realized-P&L column.

Formula per realized lot:
```
realized_market_pnl_native = (sell_price - buy_price) × qty                  [USD]
realized_market_pnl_base   = realized_market_pnl_native × sell_fx_rate       [TWD]
realized_fx_pnl_base       = buy_cost_native × (sell_fx_rate - buy_fx_rate)  [TWD]
total_realized_pnl_base    = realized_market_pnl_base + realized_fx_pnl_base
```

Both are interesting to show separately — investors want to know which contribution is market judgment vs FX timing.

**Files:**
- `Assetra.Application/Portfolio/Analysis/RealizedPnlBreakdownCalculator.cs` — new pure function
- `TradeRowViewModel` — expose breakdown
- Trade list page — new optional columns for the two components

**Acceptance:**
- Sample: bought AAPL $100/share when USD=30 TWD, sold $110/share when USD=32 TWD, 100 shares.
  - market pnl native = $1,000
  - market pnl base = $1,000 × 32 = 32,000 TWD
  - fx pnl base = $10,000 × (32 − 30) = 20,000 TWD
  - total = 52,000 TWD
- Existing all-TWD trades show fx_pnl_base = 0, no behavioral change

## Out of scope (for now)

- **Commission currency conversion** in reports — model already supports it via `Trade.CommissionCurrency`, but reports treat all commission as instrument currency; revisit when a real complex-fee 複委託 example arises
- **Multi-base export** — exporting all to a different base currency (CSV/PDF) than the in-app session base
- **Currency hedging analytics** — show portfolio FX exposure (e.g. "你 35% 部位是 USD")

## Risks

- **FX rate source reliability**: Yahoo `=X` symbols are not contractually stable. Need fallback (central bank, fixer.io, ECB). Mitigation: P4.1's `source` column lets us track and switch.
- **Snapshot schema migration on large DBs**: adding `currency` PK column requires a table rewrite if SQLite doesn't allow ALTER PK in-place. Mitigation: keep date-only PK + plain `currency` column; if dedupe needed do it at read time.
- **UI complexity creep**: every report grows a "base currency" knob. Mitigation: centralize via `AppSettings.PreferredCurrency` as default; per-screen override only when truly justified.

## Open questions

1. **Crypto position currency handling**: BTC/ETH market value is in USD by convention but the asset itself is non-fiat. Treat as instrument currency = USD? Or special-case?
2. **Real-estate / physical assets in foreign currency**: same multi-currency machinery applies, but those are not Trade rows. Plan separately or extend the snapshot per-currency tagging to include them.
3. **Multi-leg cross-currency Transfer**: e.g. TWD account → USD account (manual currency conversion booked as `Transfer`). Today this writes one `Trade` with `Type=Transfer` — does it need its own FxRate field, or do we model it as paired Withdrawal + Deposit with explicit FX?
