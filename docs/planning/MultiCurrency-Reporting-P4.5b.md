# MultiCurrency-Reporting P4.5b — Persist Realized P&L Breakdown

**Status:** ✅ shipped 2026-05-19
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** P4.5 (✅ calculator only) + P4.1e (✅ historical FX provider)

## Why this exists

P4.5 built the breakdown calculator (`RealizedPnlBreakdownCalculator.Compute`)
but it has no caller. Without a caller, no Trade row ever surfaces market
vs FX gain — the calculator is dormant. This pass wires it through
`SellWorkflowService` so every new Sell trade lands with the breakdown
computed + persisted.

UI columns on the Trades DataGrid are deferred to **P4.5c** so this pass
stays focused on the data plumbing.

## Scope

In:
- `Trade.RealizedMarketPnl` + `Trade.RealizedFxPnl` (both `decimal?`, default null)
- TradeSchemaMigrator adds the two REAL columns
- Repo + sync mapper round-trip the new fields
- `SellWorkflowService` injects `IFxRateHistoryService`, computes
  breakdown at sell time, persists on the new `Trade` record
- 1 simplification documented: buy-date FX rate uses the **earliest**
  Buy trade for the matching PortfolioEntry (FIFO approximation). True
  weighted-avg-per-lot FX is deferred — most sells close one or two lots
  and the math agrees within a fraction of a percent.

Out (P4.5c):
- DataGrid columns in TradesTabPanel showing the breakdown
- Per-period aggregation reports (monthly market vs FX totals)

## Task checklist

- [x] **B1** — `Trade` record: append `RealizedMarketPnl` + `RealizedFxPnl`
  (both `decimal? = null`). Doc comment explains they're populated at sell
  time when FX history is available.
- [x] **B2** — `TradeSchemaMigrator`: allowlist + `MigrateAddColumn` for
  the two new columns + sync metadata propagation.
- [x] **B3** — `TradeSqliteRepository`: SelectClause, MapTrade (new
  ordinals 28 / 29), BindTradeParams, InsertSql, UpdateAsync SET clause,
  ApplyRemote INSERT + ON CONFLICT UPDATE SET clauses, sync metadata
  ordinals shift from 28→30 / 29→31 / 30→32 / 31→33.
- [x] **B4** — `TradeSyncMapper`: DTO append + ToEnvelope + FromPayload.
- [x] **B5** — `SellWorkflowService`: inject `IFxRateHistoryService`
  (optional — null = skip breakdown). After computing `realizedPnl`,
  find first Buy of the PortfolioEntry, look up buy-date FX + sell-date
  FX, call calculator, attach to the Trade record before persisting.
- [x] **B6** — Plumb `IFxRateHistoryService` into the DI wiring for
  `SellWorkflowService`.
- [x] **B7** — Tests:
    - SyncMapper round-trip preserves the two new fields
    - Repo round-trip preserves them
    - SellWorkflow with FX history available → breakdown persisted
    - SellWorkflow with same-currency trade → breakdown.FxBase = 0 persisted
    - SellWorkflow with missing FX history → fields stay null
- [x] **B8** — Build + commit + plan doc final.

## Acceptance

- A new Sell trade for AAPL with FX history populated lands with
  `RealizedMarketPnl` ≈ market_native × sell_fx and `RealizedFxPnl`
  ≈ buy_cost_native × (sell_fx − buy_fx).
- All existing Sell tests still pass.
- DB upgrade idempotent (re-running schema migrator on a DB with the
  columns already present does nothing).
