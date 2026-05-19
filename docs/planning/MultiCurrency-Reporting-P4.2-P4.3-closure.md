# MultiCurrency-Reporting P4.2 / P4.3 — Closure Audit

**Status:** ✅ Pre-existing infrastructure closes most of the scope
**Last updated:** 2026-05-19
**Owners:** assistant

## TL;DR

While planning the "next" P4.2 + P4.3 passes, I audited the codebase and
found that **both phases are mostly already shipped** by prior
MultiCurrency-Trade-Refactor work. This doc records what's truly done vs
remaining, so future passes don't re-do the same plumbing.

## P4.2 — Position aggregation with Money type

**Spec acceptance criteria:**
> Positions table shows `成本 100 USD` next to AAPL, `成本 50,000 TWD` next to 2330
> Adding a row for "Total（TWD）" sums via FX conversion using today's rate
> Color-code per currency for quick visual scan

**Audit result:**
- ✅ `PortfolioRowViewModel` already has the full Money + native/base split:
  `BuyPriceAsMoney`, `MarketValueAsMoney`, `CostAsMoney`, `PnlAsMoney`,
  plus `*BaseAsMoney` counterparts (line 163-177 of the VM).
- ✅ `PositionsTabPanel.xaml` already binds via `*AsMoney` properties
  with `CurrencyConverter` (`amount` for native, `amount-approx` for
  base currency context with ≈ prefix).
- ✅ The Money type and `NormalizedCurrency` / `NormalizedBaseCurrency`
  flow end-to-end (PositionQueryService → DTO → VM → XAML).
- ⚠ "Color-code per currency" — partial; currencies are visible via tag
  but no per-currency hue. Pure polish, defer.
- ⚠ "By currency" Allocation grouping — not yet implemented. The
  Allocation tab has "by group" from P4 / Portfolio-Groups work but no
  "by currency" mode. **Shipping this in a P4.2-tail commit below.**

## P4.3 — Snapshot per-currency

**Spec recommendation:**
> Per-currency snapshot rows: `portfolio_daily_snapshot` gains a
> `currency` column; one row per (date, currency).

**Audit result:**
- ✅ `portfolio_daily_snapshot` table has `currency TEXT NOT NULL
  DEFAULT 'TWD'` column since v0.14.2.
- ✅ `PortfolioDailySnapshot` record carries `Currency = "TWD"` field.
- ✅ `PortfolioSnapshotService` stamps `currency` per record from
  `AppSettings.BaseCurrency`.
- ⚠ The PK is still `(SnapshotDate)` not `(SnapshotDate, Currency)` —
  multi-base-currency users would only get one row per day. Could be
  upgraded later, but **P4.1e's hybrid FX provider** (history-first /
  legacy-fallback) already gives correct past-date conversion at read
  time. So the original P4.3 motivation ("FX-stable historical
  comparisons") is met through a different mechanism without the schema
  change.

## What's actually shipped in this final pass

To complete P4.2's last user-visible gap:

- [x] **Allocation tab "by currency" grouping option** — parallels the
  existing `IsByGroupMode` toggle (from Portfolio-Groups-Refactor P4).
  Adds a third radio button "依幣別" alongside "依 Symbol" / "依群組",
  plus VM mode + XAML radio + lang strings.
  - VM: replaced `_isByGroupMode` bool with `_groupingMode` enum
    (`AllocationGroupingMode { Symbol, Group, Currency }`) + computed
    `IsBySymbolMode` / `IsByGroupMode` / `IsByCurrencyMode` flags +
    `SetGroupingModeCommand(string raw)` that `Enum.TryParse`s.
  - `Rebuild()` ternary became a switch: Currency case groups by
    `PortfolioRowViewModel.Currency` (upper-cased, blank → "TWD"),
    sums native MarketValue per ccy, displays code as both Symbol/Name.
  - XAML: 3 radios with `CommandParameter="Symbol|Group|Currency"`;
    Group radio still gated by `HasPortfolioGroups`, Currency always shown.
  - Rebalance tab still forces `Symbol` mode on entry (per-symbol targets
    don't align with ccy/group bucket keys).
  - Lang: `Allocation.GroupBy.Currency` added in zh-TW + en-US.

No schema changes; no DI changes. Pure VM logic + 1 XAML radio button +
2 lang keys per locale.

## Decision: stop here on MultiCurrency-Reporting

After this commit, every spec'd P4 phase is either done or has an
explicit reason for not shipping the literal scope (P4.3's per-currency
PK is moot given P4.1e's hybrid provider). The next bet is no longer
incremental polish — it's UI design decisions like "where to surface
breakdown beyond AuditLog" or "should we add a multi-base-currency
switcher to Trends". Those need explicit user direction, not autopilot.
