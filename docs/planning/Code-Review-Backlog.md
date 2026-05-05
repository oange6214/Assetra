# Code-Review Backlog

Items raised during the v0.22 → v0.23 multi-feature code review that have not yet been actioned. C/H/M/L = severity (Critical / High / Medium / Low).

Already shipped (closed): C1, C2, H2, H4, H5, H6, H7-partial, M2, M3, M4, M5, M7-partial (5 of 6 VMs covered), L1, L2, L4-partial.

**Pre-existing test-isolation bug** — the 4 `Debug.Assert(Dispatcher.CheckAccess())` sites in `PortfolioViewModel` fired spuriously after any test created a bare `System.Windows.Application` to populate WPF resources. Replaced inline check with `IsOnUiThreadOrTestEnvironment()` that distinguishes the production `App` subclass from the test-fake bare base. Full suite now 1119/1119 green.

---

## L3 — AllocationViewModel ↔ PortfolioViewModel coupling  🟡 Partial

**Effort:** 2–3h remaining (AllocationViewModel done, Dashboard + FinancialOverview pending)

✅ **AllocationViewModel:** decoupled via `IPortfolioPositionFeed` (Positions + TotalCash + INotifyPropertyChanged). 8 unit tests demonstrate substitution against a stub feed. PortfolioViewModel implements the interface through explicit member forwarding so existing direct callers keep their `ObservableCollection<T>` access.

🔲 **DashboardViewModel:** consumes 30+ proxy properties forwarding NetWorth/DayPnl/TotalAssets/TotalLiabilities/Financial/History from PortfolioViewModel. Either expose a fatter `IPortfolioDashboardSource` or inline the proxy values into a separate snapshot DTO.

🔲 **FinancialOverviewViewModel:** subscribes to `PortfolioViewModel.PropertyChanged` for `TotalMarketValue`; reads `Positions`. Smaller surface — could reuse `IPortfolioPositionFeed` plus a `TotalMarketValueChanged` notification pattern.

Both follow-ups share the existing `IPortfolioPositionFeed` skeleton — extend per consumer.

---

## L6 — Code-behind logic moves to VM/Behavior  ✅ Closed

| File | Status |
|---|---|
| `Portfolio/PortfolioView.xaml.cs` | ✅ `BackdropClickToCloseBehavior` extracted; 3 backdrop handlers gone. Escape cascade kept (legitimate keyboard-state aggregator). |
| `Portfolio/Controls/DividendCalendarPanel.xaml.cs` | ✅ 144→8 lines. Replaced with `ItemsControl` + `DataTemplate` bound to `DividendCalendarCellViewModel`. Hover/disabled style via control template triggers. |
| `Settings/SettingsView.xaml.cs` (75 lines) | ✅ Triaged: justified MVVM glue (`PasswordBox.Password` isn't a DependencyProperty, slider drag-completion is non-routed). No refactor needed. |

---

## M1 — Money value object

**Effort:** 4–6h

Domain currently passes `decimal` everywhere with implicit "TWD or whatever the user picked" semantics. Misuse risks (mixing currencies in arithmetic, formatting drift across `MoneyFormatter` / `CurrencyService` / hand-rolled `:N0` strings).

**Target:** `Assetra.Core.Money(decimal Amount, string Currency)` value record. Operator overloads only allow same-currency addition; cross-currency requires explicit `IMultiCurrencyValuationService.Convert`. Migrate ledger / repository / display incrementally — start with `IBalanceQueryService.GetBalanceAsync` returning `Money` instead of `decimal`.

**Risk:** cross-cutting; touches Core / Application / WPF / Tests. Best done in a dedicated branch with one type at a time.

---

## M6 — ObservableCollection encapsulation

**Effort:** 2–4h remaining (28 of 52 covered)

**Done:** Insurance, PhysicalAsset, RealEstate, Retirement, Goals, Alerts, Reconciliation, Fire, MonteCarlo, Import, BudgetSummaryCard, AllocationPanel, Categories (5 collections), TxVM Categories partial (Expense/Income), TradeFilter (Type/Asset).

**Pattern (canonical):**
```csharp
private readonly ObservableCollection<T> _items = [];
public ReadOnlyObservableCollection<T> Items { get; }

public Foo() {
    Items = new ReadOnlyObservableCollection<T>(_items);
    // … mutate _items internally
}
```

**Remaining (~24 sites):**
- **Portfolio** (Positions / Trades / CashAccounts / Liabilities) — cross-shared with `TransactionDialogViewModel.Trades / Positions / CashAccounts / Liabilities` by reference. Encapsulating requires Tx VM to also expose `ReadOnlyObservableCollection<T>` and the Tx VM's `CashAccountSuggestions` / `PositionSuggestions` (currently mutated by Portfolio directly) need internal mutators. Tackle together with **H1**.
- **FinancialOverview** (AssetGroups / InvestGroups / LiabGroups) + AssetGroupVm.Items — Items is row-VM populated externally; needs builder pattern. Depends on `Portfolio.Positions` so wait for L3.
- **Allocation** (AllocationRows) — touched in L3.
- **Row VMs** (LiabilityRowViewModel.ScheduleEntries, CategoryRowViewModel.EditIconOptions, AssetGroupVm.Items) — externally populated; needs constructor-time list-passing or `internal` mutators. Lower priority.
- **SellPanel.CashAccounts** — has `init {}` setter, externally assigned; works as-is.

Recommend: gate further M6 work on H1 (Tx split unblocks Portfolio root) and L3 (Allocation/FinancialOverview decoupling). The 28 done are the cleanly-isolated VMs.

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
