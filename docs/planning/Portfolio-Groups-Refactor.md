# Portfolio Groups Refactor

**Status:** P1–P6 done; only P7 (deferred polish) remains, plus a few small documented limitations
**Last updated:** 2026-05-18 (gap-closure pass)
**Owners:** assistant
**Depends on:** —
**Related:** `MultiCurrency-Trade-Refactor.md` (P1-P3 landed)

## Implementation status (2026-05-18)

| Phase | Status | Notes |
|-------|--------|-------|
| P1 — Core entity + migration | ✅ Done | `PortfolioGroup` + repo + schema column on `trade` / `asset` / `portfolio` / `financial_goal`. Backfill = `DefaultId` on every NULL row. |
| P2 — Group CRUD UI | ✅ Done | NavRail tab + `PortfolioGroupsViewModel` + add/edit dialog. System-protected default group cannot be deleted. Reorder UI (drag-drop) not in this pass. |
| P3 — Trade dialog selector | ✅ Done | DTOs (`StockBuyRequest` / `SellWorkflowRequest` / `CashDividendTransactionRequest` / `ManualAssetCreateRequest`) carry `PortfolioGroupId`. Workflow services propagate it into `Trade`. Tx dialog shows a group ComboBox above the per-type form for Buy / Sell / CashDividend / StockDividend; edit-trade restores the selection from `Trade.PortfolioGroupId`. |
| P4 — Positions filter chips | ✅ Done | Chip row above Positions DataGrid: "全部群組" + one chip per group. Position rows derive `PortfolioGroupId` from their latest Buy trade (set in `ApplyLatestTradeDiscounts`). |
| P5 — Goals linked to group | ✅ Done | `FinancialGoal.PortfolioGroupId` added; Goals dialog has a group ComboBox below LinkedAssetClass; Hero `HeroGoalProgressValue` precedence = **PortfolioGroupId → LinkedAssetClass → Manual**. Group net value comes from new `IGroupBalanceQueryService` (signed cash-flow MVP; mark-to-market deferred). |
| P6 — FIRE per-group | ✅ Done (calculator scope, no FirePlan entity) | FIRE page got a group ComboBox; picking a group auto-fills "Current Net Worth" via `IGroupBalanceQueryService`. Saved FIRE goal carries `PortfolioGroupId`. |
| P7 — Per-group dashboard | ⏸ Deferred | Not implemented in this pass per original plan ("deferred polish"). |

### Resolved during gap-closure pass (2026-05-18)

- ✅ **P2 tests** — added [`PortfolioGroupsViewModelTests`](../../Assetra.Tests/WPF/PortfolioGroupsViewModelTests.cs) covering Load, Save (new + edit), validation, Edit-restores-form, CanDelete computed, system-row protection, confirm-delete dialog yes/no. 9 new tests on top of the existing 6 repo tests = **15 group tests total**.
- ✅ **P3 `PortfolioEntry.PortfolioGroupId`** — `PortfolioEntry` record + `PortfolioSqliteRepository` (SelectClause / MapEntry / BindEntry / InsertSql / ApplyRemote upsert / sync ordinals) + `PortfolioSyncMapper` (DTO + payload round-trip) + `FindOrCreatePortfolioEntryAsync(..., portfolioGroupId)` + `AddAssetWorkflowService` propagation now all carry the group id. New entries default to `DefaultId` via repo fallback.
- ✅ **P4 Allocation "by group"** — `AllocationViewModel.IsByGroupMode` toggle; XAML radio buttons in `AllocationView.xaml` switch the rebuild key between `Symbol` (default) and `PortfolioGroupId`. Treemap/list rebuild swaps `groupBy` accordingly.
- ✅ **`AssetSchemaMigrator.BackfillPortfolioGroupId`** — added so existing cash/asset rows pick up `DefaultId` on upgrade (was previously only on `trade` / `portfolio` / `financial_goal`).
- ✅ **Hero refresh on trade events** — `RaiseCompositionChanged()` now also fires `_ = RefreshGroupNetValuesAsync()`, so Hero goal progress updates in real time after any trade.

### Remaining known gaps

**Minor acceptance-criteria deltas** (workable behaviour, spec phrasing not literally met):

- **P2 reorder** — UI has Add/Edit/Delete only; `SortOrder` is set on creation but no drag-drop UI. Sort order is editable via the form's hidden field flow, not visually.
- **P3 last-used group memory** — spec says "auto-defaults to 預設群組 **or last-used**". Implementation always defaults to DefaultGroup on dialog open; no per-session memory.
- **P4 per-group subtotals in DataGrid header** — spec phrases as "per-group subtotal headers **or** filter chips". Chips + "By Group" allocation mode ship; no subtotal headers in the positions DataGrid itself.

**Deferred / documented limitations**:

- **`IGroupBalanceQueryService` MVP only sums signed cash flows** (buy − / sell + / income + / dividend + / withdraw −). It doesn't add unrealized market value of held positions. Replace with `position MV + cash balance` when `PortfolioEntry`/`AssetItem` carry group natively.
- **`AssetItem.PortfolioGroupId` column** exists + backfill DefaultId on migration, but `AssetItem` model + repo don't read/write it. Cash account → group mapping unused on read path (only Trade.PortfolioGroupId drives the flow today).
- **Index on `*.portfolio_group_id`** not added (Open question #3). Add when query times degrade.
- **Cross-group transfer** flows through the existing Withdrawal/Deposit pair pattern; no special handling — both legs default to their respective trade's group choice.
- **P7 per-group dashboard view switching** — explicitly deferred polish per original plan.

## Why this exists

Today every Trade record knows *what* (symbol), *when* (date), *how much* (price × qty), and *paid from* (cash account) — but not *which mental bucket the user is saving toward*. That gap propagates:

- **Goal progress** has no automatic source. `FinancialGoal.CurrentAmount` is a manually-typed decimal the user must keep editing. Most users let it bitrot within a week; the progress bar becomes decorative.
- **FIRE calculation** runs over the entire net worth. A user with a「short-term active trading」portfolio and a「long-term retirement」portfolio gets a single blended FIRE date that doesn't match either mental model.
- **Risk allocation** is monolithic. Can't say「I'm OK with 80% stocks in my retirement bucket, but only 30% in my買房頭期款 bucket」.
- **Tax / accounting handling** is harder. Real brokerages keep separate sub-accounts; modelling that requires per-bucket position state.

Every mature personal-finance app solves this with a **Portfolio / Sub-Account / Bucket** abstraction:

| App | Abstraction |
|-----|-------------|
| IBKR | Sub-Accounts (multi-level) |
| Vanguard | Multiple portfolios per login |
| Fidelity | Sub-account groupings |
| YNAB | Categories with target/age tracking |
| Monarch Money | Goals linked to accounts |
| Mint | Goals linked to accounts |

Assetra currently sits in the「single global bucket」camp. This doc lays out the migration.

## Conceptual model

```
Portfolio (群組)                     ──┬── Id                  Guid (PK)
                                      ├── Name                "退休帳戶"
                                      ├── Color               #3B82F6 (display tint)
                                      ├── DefaultCashAccountId Guid?   (買賣時預設扣款 / 入款)
                                      ├── Description         "我的長期 FIRE 帳戶"
                                      ├── IconKey             "PersonClock24" (optional)
                                      ├── SortOrder           int
                                      ├── CreatedAt / UpdatedAt
                                      └── (sync columns: version, last_modified_*, is_deleted, is_pending_push)

Trade                ──┬── PortfolioId  Guid?   (FK, nullable for legacy / non-investment)
                       └── (existing columns)

PortfolioEntry       ──┬── PortfolioId  Guid?   (FK)
                       └── (existing columns)

AssetItem            ──┬── PortfolioId  Guid?   (FK; only meaningful for Cash type)
                       └── (existing columns)

FinancialGoal        ──┬── PortfolioId  Guid?   (FK; goal tracked against this group's net value)
                       │   OR
                       ├── LinkedAssetClass  string?  (interim solution; phased out once Portfolio entity ships)
                       └── (existing columns)

FirePlan             ──┬── PortfolioId  Guid?   (FK; FIRE calc scoped to this group)
                       └── (existing columns)
```

The `*PortfolioId` columns are all **nullable** because:
- Legacy rows (pre-refactor) have no value
- Some transactions are genuinely cross-group (e.g. a Transfer between cash accounts owned by different portfolios — needs explicit handling, see Open Questions)
- Investment-only abstraction; non-investment cash flows (Income / Expense Withdrawal) may stay un-grouped

## Why group-based goal progress works automatically

```
goalProgress(goal) :=
    let group = portfolios[goal.PortfolioId]
    let groupAssets = Σ(positions where pos.PortfolioId == group.Id).MarketValue
                    + Σ(cash where cash.PortfolioId == group.Id).Balance
                    + Σ(retirement where retirement.PortfolioId == group.Id).TotalBalance  // if extended
    return groupAssets / goal.TargetAmount × 100
```

Every Buy / Sell / Transfer that affects a position or cash account in the group automatically updates `groupAssets` via the existing balance-query pipelines. **No user maintenance needed**. The goal value tracks real money.

## Phase plan

### **P1 — Core entity + migration (low risk, no UI yet)**

**Goal:** `Portfolio` entity exists, all related rows can carry `PortfolioId`. Behaviour unchanged.

**Files:**
- `Assetra.Core/Models/Portfolio.cs` — new immutable record
- `Assetra.Core/Interfaces/IPortfolioGroupRepository.cs` — CRUD interface
- `Assetra.Infrastructure/Persistence/PortfolioGroupSchemaMigrator.cs` — new table
- `Assetra.Infrastructure/Persistence/PortfolioGroupSqliteRepository.cs` — impl
- Migration steps in `TradeSchemaMigrator` / `PortfolioSchemaMigrator` / `AssetSchemaMigrator` / `GoalSchemaMigrator` / `FirePlanSchemaMigrator` — add nullable `portfolio_id` column to each
- Backfill: create one **"預設群組"** (default group), reassign all existing trades / positions / cash accounts to it. Goals + FIRE plans stay null (user opts in later).

**Acceptance:**
- DB upgrade from v0.x succeeds, all existing rows have `portfolio_id = <default-group-guid>` for investment-related; null for non-investment.
- All existing tests pass without behavioural change.
- No UI change visible to user.

### **P2 — Group CRUD UI (low risk, isolated tab)**

**Goal:** User can create / edit / delete / reorder groups. No other page touches groups yet.

**Files:**
- `Assetra.WPF/Features/PortfolioGroups/GroupsViewModel.cs`
- `Assetra.WPF/Features/PortfolioGroups/GroupsView.xaml(.cs)`
- Add「群組」leaf under「分析」or「資產」 NavRail node
- `Assetra.WPF/Languages/zh-TW.xaml` + `en-US.xaml` strings
- `Assetra.WPF/Infrastructure/AppBootstrapper.cs` — DI registration

**Acceptance:**
- User can add a group「退休帳戶」+ color + linked bank account.
- Edit / delete / reorder work.
- Default group exists post-migration; user can rename but not delete (system-protected).
- Add 5+ unit + integration tests.

### **P3 — Trade dialog gets group selector**

**Goal:** Buy / Sell / CashDividend dialogs allow selecting destination group.

**Files:**
- `Assetra.WPF/Features/Portfolio/SubViewModels/Tx/BuyTxViewModel.cs` — add `SelectedGroup` property
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml` — add ComboBox
- Same for `SellTxForm.xaml` / `CashDividendTxForm.xaml`
- `Assetra.Application/Portfolio/Dtos/AddAssetWorkflowDtos.cs` — add `PortfolioId` to `StockBuyRequest`
- `Assetra.Application/Portfolio/Dtos/SellWorkflowRequest.cs` — add `PortfolioId`
- `Assetra.Application/Portfolio/Services/*WorkflowService.cs` — propagate `PortfolioId` to `Trade` record
- Edit-trade flow restores group selection from existing record

**Acceptance:**
- New Buy auto-defaults to「預設群組」or last-used group (per session).
- Selected group propagates: `Trade.PortfolioId` saved, `PortfolioEntry.PortfolioId` saved.
- Edit existing trade pre-selects its group.

### **P4 — Position views grouped by Portfolio**

**Goal:** Investment Assets tab can filter by group; per-group subtotals visible.

**Files:**
- `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml` — group filter strip above table
- `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` — `SelectedGroupFilter` property + filtered view
- `Assetra.Application/Portfolio/Services/PositionQueryService.cs` — return positions tagged with group
- Allocation tab: add「by group」grouping option alongside「by asset class」

**Acceptance:**
- DataGrid shows per-group subtotal headers or filter chips.
- Switching group filter recomputes table quickly (no DB roundtrip).

### **P5 — Goal linked to Portfolio (auto-tracking)**

**Goal:** Goals reference a group; progress = group's current net value.

**Files:**
- `Assetra.Core/Models/FinancialGoal.cs` — add `PortfolioId: Guid?` (keep `CurrentAmount` for backward compat / manual mode)
- `Assetra.Application/Goals/GoalProgressQueryService.cs` — when `PortfolioId` set, compute `currentAmount` from group balance
- `Assetra.WPF/Features/Goals/GoalsView.xaml` — add「連結到群組」ComboBox in add/edit dialog
- `Assetra.WPF/Features/Goals/GoalsViewModel.cs` — track `SelectedGroup`, sync to model
- `Assetra.WPF/Features/FinancialOverview/FinancialOverviewViewModel.cs` — Hero `PrimaryGoal` progress reads computed value, not stored `CurrentAmount`

**Acceptance:**
- Linking goal「買房 NT$3M」to group「買房儲蓄」: progress auto-updates as user buys/sells/deposits.
- Existing「Manual」goals continue working (when `PortfolioId == null`).
- Hero card reflects group-based progress in real time after any trade.

### **P6 — FIRE per-portfolio**

**Goal:** FIRE plan scoped to one group; results reflect that group's net worth + saving rate.

**Files:**
- `Assetra.Core/Models/FirePlan.cs` — add `PortfolioId: Guid?`
- `Assetra.WPF/Features/Fire/FireViewModel.cs` — `SelectedGroup` filter
- `Assetra.WPF/Features/Fire/FireView.xaml` — group selector

**Acceptance:**
- User with「退休帳戶」group can compute FIRE date based only on that group's assets + saving rate.
- Global FIRE (no group selected) still works.

### **P7 — Per-group dashboard (deferred polish)**

**Goal:** Switch the entire Financial Overview between「全部」 / 「per-group」 views.

**Out of scope for initial rollout** — done as a polish phase once P1-P6 are in production.

## Default group + migration strategy

On first launch after migration:

1. Create **「預設群組 (Default)」** with `Id = '00000000-0000-0000-0000-000000000001'` (stable seed Guid).
2. All existing `Trade` / `PortfolioEntry` / `AssetItem (Cash)` rows: `portfolio_id = <default>`.
3. `FinancialGoal` / `FirePlan` rows: leave `portfolio_id = null` (user opts in via dialog).
4. Default group is **system-protected** — can rename, cannot delete.

Optional smarter migration (later iteration): split default group by some heuristic (e.g. exchange-based —「台股」/「美股」/「加密」 default groups), but the safe MVP keeps everything in one bucket.

## Trade-offs we accept

- **Manual goal mode stays available** (`FinancialGoal.PortfolioId == null` + manual `CurrentAmount`) so use cases like「年度旅遊預算 NT$50K」(non-investment-asset-backed) still work.
- **Cross-portfolio transactions**: e.g., Transfer from「投資 A 群組現金」to「投資 B 群組現金」. Modelled as Withdrawal from A + Deposit into B (already the conceptual model for `TransactionWorkflowService.Transfer`).
- **Liabilities are not grouped** (initial scope). Debt is a household-level concept; per-group debt would over-complicate things. Liability ratio in Hero / 公式列 stays global.
- **Multi-group per goal**: only support 1:1 in P1-P5. Many-to-many (one goal funded by multiple groups) is a future enhancement.

## Open questions

1. **Sync (multi-device)**: every Trade-related table already has sync columns. Should Portfolio entity also be sync'd? — YES, ensures group name / color stay consistent across devices.
2. **Group reassignment**: if user moves a trade from group A to group B, do we move the position too? Yes — but the corresponding PortfolioEntry's `PortfolioId` should be updated atomically with the Trade's. Need an `IAtomicReassignService`.
3. **Performance**: queries that now filter by `portfolio_id` should add an index on each table's `portfolio_id` column.
4. **Cash account double-purpose**: today CashAccount.Id is referenced by many trade types. If we let CashAccount.PortfolioId differ from Trade.PortfolioId, what does that mean? Recommendation: enforce or warn that cash account's group == trade's group.

## Estimated effort

| Phase | Engineering days |
|-------|-----------------|
| P1 | 2-3 |
| P2 | 1-2 |
| P3 | 2-3 |
| P4 | 2-3 |
| P5 | 2 |
| P6 | 1-2 |
| **Total to feature-complete (P1-P6)** | **10-15 days** |
| P7 polish | +2-4 |

## Short-term compromise (already implemented as interim)

While the full refactor lands, an **interim solution** ships now (separate commit):

- `FinancialGoal.LinkedAssetClass: string?` — enum-like values: `"NetWorth"`, `"Investments"`, `"Cash"`, `"RealEstate"`, `"Retirement"`, `"Physical"`, or null (manual)
- When set: goal progress auto-computes from the matching aggregate property on `FinancialOverviewViewModel` (e.g. `BalanceSheetTotalAssets` for `"NetWorth"`)
- Forward-compatible: when P5 ships, migrate by mapping `LinkedAssetClass="Investments"` → goal linked to「投資 default group」, etc.
- Avoids requiring users to wait 2-4 weeks for any auto-tracking
