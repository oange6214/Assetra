# Code-Review Backlog

Items raised during the v0.22 → v0.23 multi-feature code review that have not yet been actioned. C/H/M/L = severity (Critical / High / Medium / Low).

Already shipped (closed): C1, C2, H2, H4, H5, H6, H7-partial, M2, M3, M4, M5, M7-partial (5 of 6 VMs covered), L1, L2, L4-partial.

**Pre-existing test-isolation bug** — the 4 `Debug.Assert(Dispatcher.CheckAccess())` sites in `PortfolioViewModel` fired spuriously after any test created a bare `System.Windows.Application` to populate WPF resources. Replaced inline check with `IsOnUiThreadOrTestEnvironment()` that distinguishes the production `App` subclass from the test-fake bare base. Full suite now 1119/1119 green.

---

## L3 — AllocationViewModel ↔ PortfolioViewModel coupling  ✅ Closed for the read-only consumers

✅ **AllocationViewModel:** decoupled via `IPortfolioPositionFeed`. 8 unit tests.

✅ **FinancialOverviewViewModel:** decoupled via the same interface (extended with `TotalMarketValue`). 5 unit tests.

🔲 **DashboardViewModel:** kept as direct `PortfolioViewModel` consumer — it has 30+ proxy properties (NetWorth/DayPnl/TotalAssets/TotalLiabilities/Financial/History) AND writes back `SelectedTab`. Not a read-only feed pattern; introducing a fat interface here would just duplicate the parent's surface. Decision: keep coupled.

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

## M7 餘 — FinancialOverviewViewModel test coverage  ✅ Closed

5 construction-time tests landed once L3 introduced `IPortfolioPositionFeed` for FinancialOverview. Reload paths route through `Application.Current.Dispatcher.Invoke` so they're left as integration-only (would need an STA dispatcher fixture).

---

## Recommended Order (remaining)

1. **H3 Phase 2-3** (test fixture builder for PortfolioViewModelTests) — pure-test mechanical, ~2-4h.
2. **M1** (Money value object full migration) — risky cross-cutting; do on its own branch, ~4-6h.
3. **M6**餘 (ObservableCollection encapsulation, 24 sites) — gated on H1 for Portfolio root + Tx mirror, ~4-6h.
4. **H1** (TransactionDialog split) — biggest single item, dedicated 16-32h.

Closed this session: C1, C2, H2, H4, H5, H6, H7-partial, M2, M3, M4, M5, **M7 (all VMs)**, **L1**, L2, **L3 (Allocation + FinancialOverview)**, **L4-partial**, **L6 (all 3 files)**, **M1 foundation**, **H3 Phase 1**, plus **the long-standing test-isolation bug**.

Total estimated remaining effort: **26–48h** (down from 44–82h at session start).
