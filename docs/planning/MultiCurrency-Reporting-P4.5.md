# MultiCurrency-Reporting P4.5 — Realized P&L Market vs FX Breakdown

**Status:** ✅ shipped 2026-05-19 (calculator + tests only; UI columns deferred to P4.5b)
**Last updated:** 2026-05-19
**Owners:** assistant
**Parent:** `MultiCurrency-Reporting.md` P4.5
**Depends on:** P4.1 (FX history store, ✅ shipped)

## Why this exists

When a user sells a foreign-currency position (e.g. AAPL bought at $100
when USD=30 TWD, sold at $110 when USD=32 TWD), the realized gain has
two components:
- **Market gain** (USD): the stock moved $10/share → $1,000 on 100 shares
- **FX gain** (TWD): USD appreciated 30→32 → $10,000 cost now buys 20,000 TWD more

Today the trade row shows one blended `RealizedPnl` in account currency
without separating which was investing skill vs FX timing. P4.5 splits them.

## Formula

```
realized_market_pnl_native = (sell_price - buy_price) × qty                  [USD]
realized_market_pnl_base   = realized_market_pnl_native × sell_fx_rate       [TWD]
realized_fx_pnl_base       = buy_cost_native × (sell_fx_rate - buy_fx_rate)  [TWD]
total_realized_pnl_base    = realized_market_pnl_base + realized_fx_pnl_base
```

Edge cases:
- Same-currency trades (TWD bought, TWD sold) → fx_pnl_base = 0
- Missing buy or sell FX rate → return null breakdown (UI shows "—")
- Multiple buy lots feeding one sell (FIFO) → caller passes weighted avg buy cost + avg buy FX

## Scope

In:
- `RealizedPnlBreakdownCalculator` pure static class in
  `Assetra.Application/Analysis/`
- Takes raw inputs (sell_price, buy_avg_price, qty, sell_fx, buy_fx, ...)
  so it has zero infra dependencies and is trivially testable
- Returns a record `RealizedPnlBreakdown(MarketNative, MarketBase, FxBase, TotalBase)?`
  with null when inputs are insufficient

Out (P4.5b / later):
- UI columns on trades DataGrid — adding this requires DataGrid layout
  changes + lang strings, defer until calculator is solid
- Aggregation per period (monthly market vs fx totals) — separate report

## Task checklist

- [x] **P1** — Core: `Assetra.Core/Models/Analysis/RealizedPnlBreakdown.cs` record
- [x] **P2** — Application: `RealizedPnlBreakdownCalculator` static class
- [x] **P3** — Tests covering:
    - Spec example (AAPL 100→110, USD 30→32, 100 shares)
    - Same-currency trade → fx = 0
    - Missing buy_fx OR sell_fx → null breakdown
    - Zero quantity → null
    - Loss + FX gain (negative market + positive fx, total could go either way)
- [x] **P4** — Build + commit + plan doc final update.

## Acceptance

- AAPL example yields: market_native = $1000, market_base = 32000 TWD,
  fx_base = 20000 TWD, total_base = 52000 TWD.
- Same-currency input has fx_base = 0 and total_base = market_base.
- Insufficient FX inputs return null (UI can render "—").
- All new tests pass.
