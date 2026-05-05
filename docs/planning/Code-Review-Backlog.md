# Code-Review Backlog

Items raised during the v0.22 → v0.23 multi-feature code review that have not yet been actioned. C/H/M/L = severity (Critical / High / Medium / Low).

Already shipped (closed): C1, C2, H2, H4, H5, H6, H7-partial, M2, M3, M4, M5, M7-partial (5 of 6 VMs covered), L1, L2, L4-partial.

---

## L3 — AllocationViewModel ↔ PortfolioViewModel coupling

**Effort:** 4–6h

`AllocationViewModel` is a constructor-time dependent of `PortfolioViewModel` (`new AllocationViewModel(sp.GetRequiredService<PortfolioViewModel>(), …)`). It subscribes to `Positions.CollectionChanged` and individual property changes on `PortfolioRowViewModel` to recompute its bars.

**Target:** introduce `IPortfolioSnapshotProvider` (read-only stream of position summaries) + `IPortfolioPositionFeed` (CollectionChanged proxy). `PortfolioViewModel` *implements* both; `AllocationViewModel` consumes only the interfaces. Result: `AllocationViewModel` becomes unit-testable without constructing PortfolioViewModel.

Same surgery applies to `DashboardViewModel` and `FinancialOverviewViewModel` — pair this work with H3 (factory extraction) so the new interfaces drop into both code paths.

---

## L6 — Code-behind logic moves to VM/Behavior

**Effort:** 4–6h

| File | Issue | Target |
|---|---|---|
| `Portfolio/Controls/DividendCalendarPanel.xaml.cs` (144 lines) | Builds 12 month-cell buttons procedurally, applies styles via `FindResource`, attaches click handlers | Replace with `ItemsControl` bound to a `ObservableCollection<DividendMonthCellVm>` exposed by `PortfolioViewModel.DivCalendar`. Style + click via DataTemplate. |
| `Portfolio/PortfolioView.xaml.cs` (124 lines) | 3 nearly-identical backdrop-click handlers + Escape-key close cascade | New `BackdropClickToCloseBehavior` (attached prop) replaces the 3 handlers. Escape cascade stays in code-behind unless we add a generic `EscapeChainBehavior` — judgment call when implementing. |
| `Settings/SettingsView.xaml.cs` (75 lines) | Triage when implementing | tbd |

---

## M1 — Money value object

**Effort:** 4–6h

Domain currently passes `decimal` everywhere with implicit "TWD or whatever the user picked" semantics. Misuse risks (mixing currencies in arithmetic, formatting drift across `MoneyFormatter` / `CurrencyService` / hand-rolled `:N0` strings).

**Target:** `Assetra.Core.Money(decimal Amount, string Currency)` value record. Operator overloads only allow same-currency addition; cross-currency requires explicit `IMultiCurrencyValuationService.Convert`. Migrate ledger / repository / display incrementally — start with `IBalanceQueryService.GetBalanceAsync` returning `Money` instead of `decimal`.

**Risk:** cross-cutting; touches Core / Application / WPF / Tests. Best done in a dedicated branch with one type at a time.

---

## M6 — ObservableCollection encapsulation

**Effort:** 6–12h

52 `public ObservableCollection<T>` properties across feature VMs are exposed as concrete `ObservableCollection<T>` rather than `IReadOnlyObservableCollection<T>` (or `INotifyCollectionChanged + IReadOnlyList<T>`). External callers can mutate them directly, breaking the VM's invariants.

**Target:** expose `IReadOnlyList<T>` + an event surface (`INotifyCollectionChanged`). Mechanical refactor across feature pages. Run after H1 (TransactionDialog split) and L3 (Allocation decoupling) so we're not editing files that are about to be torn apart.

Could be done feature-by-feature rather than as one mega commit — recommend per-feature commits.

---

## H1 — TransactionDialog god-object split

See [H1-TransactionDialog-Split-Plan.md](./H1-TransactionDialog-Split-Plan.md) for the detailed plan.

---

## H3 — PortfolioViewModelFactory extraction

See [H3-PortfolioViewModelFactory-Plan.md](./H3-PortfolioViewModelFactory-Plan.md).

---

## M7 餘 — FinancialOverviewViewModel test coverage

**Effort:** 2–4h

Currently skipped because `FinancialOverviewViewModel` constructor takes a fully-wired `PortfolioViewModel`. Two paths:

1. Wait for L3 (introduces `IPortfolioSnapshotProvider`) and test `FinancialOverviewViewModel` against a stub provider.
2. Build a `PortfolioViewModelTestFactory` (per H3 Phase 3) and reuse it.

Path 1 is cleaner; recommend deferring until L3 lands.

---

## Recommended Order

1. **H3** (factory) — unblocks better test ergonomics for everything downstream.
2. **L3** (Allocation/Dashboard/FinancialOverview decoupling) — pairs with H3 since both touch DI wiring.
3. **M7 餘** — drops out for free once L3 is done.
4. **L6** (code-behind moves) — independent.
5. **H1** (TransactionDialog split) — biggest single item; do after L3+H3 so the new VMs benefit from the factory pattern.
6. **M1** (Money value object) — risky cross-cutting work; do last, on its own branch.
7. **M6** (ObservableCollection encapsulation) — mechanical; do feature-by-feature whenever each feature stabilizes.

Total estimated remaining effort: **44–82h** (was 44–82h before this round; no net change but the order is clearer now).
