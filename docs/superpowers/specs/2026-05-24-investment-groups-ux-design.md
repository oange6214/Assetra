# Investment Groups UX Design

**Status:** Proposed
**Date:** 2026-05-24
**Scope:** Investment assets page, group management, group-level trends, and transaction-driven aggregation

## Decision

Assetra should treat investment groups as user-defined strategy buckets. A group answers "why do I hold these assets?", while asset type answers "what is this asset?" and market/currency answers "where and how is it traded?".

Version 1 uses one primary group per investment asset. A single position row belongs to one group at a time. Lot-level or transaction-level multi-group allocation is intentionally out of scope for this phase.

## Current Context

The domain already has the core model:

- `PortfolioGroup` represents a user-defined bucket such as retirement, house savings, or short-term trading.
- `PortfolioEntry.PortfolioGroupId` stores the position-level group assignment.
- `Trade.PortfolioGroupId` stores the group context at transaction time.
- The positions page already has group filter chips, but the current UX still feels like one flat list of all assets.
- Allocation analysis can group by symbol, group, or currency, but the group concept is not yet first-class in the investment assets workflow.

This design does not create a second grouping model. It makes the existing `PortfolioGroup` concept visible, understandable, and useful.

Known implementation gap: the current positions projection may infer a visible row's group from recent trades. The target source of truth for the asset's primary group should be `PortfolioEntry.PortfolioGroupId`, with `Trade.PortfolioGroupId` preserving historical transaction context. If multiple lots of the same symbol/exchange/type have conflicting group IDs, version 1 should surface the row as needing group resolution instead of silently picking one.

## Product Model

Assetra should expose three separate axes:

| Axis | Meaning | Examples | UX Role |
| --- | --- | --- | --- |
| Asset type | What the product is | Stock, ETF, fund, bond, crypto, precious metal | Filter, badge, reporting dimension |
| Market / currency | Where it trades and how it is valued | TWSE, TPEX, NASDAQ, USD, TWD | Quote routing, FX, operational filter |
| Investment group | Why the user owns it | Long-term, cash-flow strategy, retirement, short-term trading | Primary strategy organization |

Do not use investment groups to represent market or asset type. Users can create groups named "US growth" or "Taiwan dividend", but the system should still preserve market/currency/type as separate objective metadata.

## User Goals

The investment group experience should let users:

- Create abstract strategy buckets in their own language.
- Assign an investment asset to one primary group.
- View all holdings as a flat list when needed.
- View holdings grouped by strategy, market, or asset type.
- Open a group and see its own value, cost, P/L, trend, holdings, and transactions.
- Understand whether trends are asset-level, group-level, or total investment-level.
- Add a transaction from a group or asset context without reselecting known information.

## Navigation And Page Structure

### Investment Assets Page

The investment assets page should become an investment cockpit, not only a table.

Top KPIs:

- Total market value
- Total paid cost
- Net P/L estimate
- Today change

Primary view mode:

- All
- By group
- By market
- By type

Secondary filters:

- Search
- Hide empty
- Show archived
- Asset type chips
- Market/currency filters

Default behavior:

- If the user has no custom groups, default to `All`.
- If the user has at least one custom group, default to `By group`.
- Ungrouped assets appear under `Ungrouped`.

### By Group View

Each group appears as a section:

```text
Long-term investment
Market value | Paid cost | Net P/L estimate | Allocation | Trend
  3231  Wistron
  0056  Yuanta High Dividend

Cash-flow strategy
Market value | Paid cost | Net P/L estimate | Allocation | Trend
  00878  Cathay Sustainable High Dividend

Ungrouped
  DRAM  Roundhill Memory ETF
```

Group sections should support:

- Collapse / expand
- Quick add asset / transaction
- Open group detail
- Menu action to move an asset to another group; drag-and-drop is out of scope for version 1.

### Group Detail

Opening a group should show a focused detail page or side panel:

- Header: group name, color/icon, description, edit action
- KPI cards:
  - Market value
  - Paid cost
  - Net P/L estimate
  - Today change
  - Allocation percentage
- Trend chart:
  - Group market value
  - Group cost
  - Group net P/L
- Holdings table:
  - Symbol/name
  - Type
  - Market/currency
  - Quantity
  - Current price
  - Market value
  - Net P/L estimate
- Transaction list filtered to this group
- Dividend/cash-flow timeline is out of scope for version 1, but the group transaction list must preserve enough data for it.

## Group Management UX

Group management should be reachable from:

- Investment assets page action menu
- Group filter/view header
- Settings only as a secondary management location

Fields:

- Name
- Color
- Icon
- Description
- Sort order
- Default cash account
- Optional target allocation percentage
- Archived state

Rules:

- The system default group exists but should not visually compete with user-created groups.
- Users cannot delete the system default group.
- Deleting a user group should require moving its assets to another group or to `Ungrouped`.
- Archived groups should remain available in historical reports but hidden from normal pickers by default.

## Assigning Assets To Groups

Supported entry points:

1. From asset detail:
   - Show current group.
   - Allow `Move to group`.

2. From investment assets list:
   - Row action: `Move to group`.
   - Later phase: multi-select and bulk move.

3. From group detail:
   - `Add asset to group`.
   - New investment transaction opened from this group should preselect the group.

4. From transaction dialog:
   - If the transaction is opened from a group context, use that group.
   - If opened from an asset context, use the asset's current group.
   - If opened globally, use default group or last-used group only if that behavior is explicit in UI.

Assignment behavior:

- Updating an asset's primary group updates `PortfolioEntry.PortfolioGroupId`.
- New buy transactions for that asset default to the asset group.
- Historical trades keep their original `Trade.PortfolioGroupId` unless the user explicitly runs a migration/reassignment action.
- A row representing multiple portfolio entries must not mix multiple active group IDs without warning; the UI should ask the user to choose one primary group for the aggregated position.

## Trends And Aggregation

Trend capabilities should be built from transaction, quote, and FX data:

```text
Trades
  -> quantity, paid cost, fees, realized cash flows, dividends
Quotes
  -> current and historical market values
FX history
  -> base-currency valuation for cross-currency holdings
PortfolioGroupId
  -> asset and transaction grouping
```

### Single Asset Trend

Shows:

- Price
- Market value
- Paid cost
- Net P/L estimate
- Dividend/cash-flow events
- Return percentage

### Group Trend

Shows aggregated values for all active assets in the group:

- Group market value
- Group paid cost
- Group net P/L estimate
- Group today change
- Group dividends/cash flows
- Optional later: group XIRR/TWR

### Total Investment Trend

Shows all investment assets:

- Total market value
- Total paid cost
- Total net P/L estimate
- Total daily change
- Long-term investment value curve

### Historical Accuracy

For historical trends, group assignment has two possible interpretations:

1. Current membership aggregation:
   - Past values are rebuilt using the assets currently in the group.
   - Simple and matches how users usually inspect today's strategy bucket.

2. Historical membership aggregation:
   - Past values use each trade's group at the time.
   - More accurate for audit/reporting but more complex.

Version 1 should use current membership aggregation for UI trends and preserve trade-level group data for future historical reports.

## Transaction Integration

Transaction entry should reduce repeated choices:

- From asset detail: asset is locked, group is inferred from the asset.
- From group detail: group is locked or preselected, asset remains selectable.
- From global add menu: no locked context; user chooses asset, then group is inferred or selectable.

For buy/sell/dividend:

- Asset selector is hidden when the dialog was launched from a known asset.
- Group selector is hidden or reduced to a compact context badge when the group is inferred.
- Advanced group override can remain available behind an edit affordance if needed.

## Allocation Analysis

Allocation should support at least:

- By asset
- By investment group
- By market/currency
- By asset type

In group mode, the chart/table should show:

- Group name
- Market value
- Actual percentage
- Target percentage, if configured
- Drift
- Suggested rebalance action

Cash should not be mixed into investment group allocation unless the user explicitly opts into group-level cash buckets. Default investment allocation should focus on investment assets only.

## Empty States

When no groups exist:

- Explain groups as strategy buckets.
- Primary action: `Create investment group`.
- Secondary action: `Continue with flat asset list`.

When a group has no assets:

- Show centered hint: `No assets in this group yet`.
- Primary action: `Add asset`.
- Secondary action: `Move existing asset`.

When assets are ungrouped:

- Show an `Ungrouped` section.
- Provide `Assign group` actions without forcing cleanup.

## Naming

Preferred Traditional Chinese labels:

- Investment group: `投資群組`
- Strategy bucket: `策略群組` only in helper text, not as the primary feature name
- Ungrouped: `未分組`
- Move to group: `移至群組`
- Group detail: `群組詳情`
- By group: `依群組`

Avoid:

- `分類` for this feature, because categories already exist for income/expense.
- `投資組合` as the group name, because the page itself already represents investment assets/portfolio.

## Non-Goals For Version 1

- Multi-group allocation for one asset.
- Lot-level group split for the same symbol.
- Rewriting all historical trades when an asset is moved.
- Full group-level tax reporting.
- Automatic strategy classification.
- Separate group-level cash ledgers unless explicitly designed later.

## Acceptance Criteria

- Users can create, edit, archive, and list investment groups.
- Users can assign each investment asset to one primary group.
- Investment assets page can switch between flat, group, market, and type views.
- `By group` view shows group subtotals and ungrouped assets.
- Asset detail shows and can update group assignment.
- Launching a transaction from asset detail preserves the asset and group context.
- Launching a transaction from group detail preserves the group context.
- Group detail shows KPI, trend, holdings, and transactions.
- Allocation analysis can group by investment group without mixing market/type semantics.
- Tests cover group assignment persistence, filtering, and transaction preselection.

## Recommended Implementation Order

1. Confirm terminology and UX copy.
2. Add group management entry point and polish existing group CRUD.
3. Add `By group / By market / By type / All` view mode to investment assets.
4. Add group section subtotals and `Ungrouped`.
5. Add asset detail group assignment.
6. Wire transaction launch context from asset and group.
7. Add group detail view with holdings and transactions.
8. Add group trend aggregation.
9. Extend allocation analysis group view.
10. Add tests and release-gate screenshots.
