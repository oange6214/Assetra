# Responsive UI Review

This note records the current responsive/font-scaling status of `Assetra`, the changes made in this pass, and the remaining UI areas that still deserve redesign rather than simple scaling.

## Completed In This Pass

- Expanded `UiScale` coverage from the main content-only region to the main shell root so the title bar and status bar scale with the rest of the app.
- Applied saved `UiScale` to standalone windows:
  - `AddStockDialog`
  - `SplashScreen`
- Increased nav rail affordance sizes to better survive larger UI scale.
- Reworked summary card rows to wrap instead of compress:
  - `PositionsTabPanel`
  - `AccountsTabPanel`
  - `LiabilityTabPanel`
- Made high-risk `DataGrid` surfaces more tolerant by enabling horizontal scroll where clipping was previously unavoidable:
  - `TradesTabPanel`
  - `AlertsView`
  - `AccountsTabPanel`
  - `LiabilityTabPanel`
- Reworked `Alerts` add form from one rigid horizontal row into a wrapped field layout.
- Reworked `SellPanel` into wide/compact modes so the quick-sell surface remains readable under larger `UiScale`.
- Reworked `SettingsView` sections into stacked, wrapping form groups so labels, helper copy, selectors, and API key controls stay readable at larger `UiScale`.
- Relaxed fixed dialog/help sizing:
  - `AddStockDialog`
  - `FugleHelpDialog`
- Replaced several literal icon font sizes in shared hotspots with typography tokens.
- Added dual-mode table/card layouts for:
  - `TradesTabPanel`
  - `PositionsTabPanel`
  - `RebalanceDataGrid`
- Reworked `AllocationView` overview and rebalance header blocks to stack and wrap instead of relying on dense side-by-side rows.
- Added a compact stacked overview mode for `AllocationView` so the treemap and allocation details remain readable under larger `UiScale`.
- Added a dual-mode table/card layout for `AlertsView` main rule list.
- Made `PortfolioView` detail side panels adapt to available width instead of using a fixed `520` pixel panel.
- Replaced the loan schedule mini-table with stacked timeline-style payment cards inside the liability detail panel.

## Responsive Design Principles Adopted

- Prefer `WrapPanel` or stacked groups for dashboard cards instead of `UniformGrid` when the card count is small and content can grow.
- Prefer scrollable tables over clipped tables when a compact card/list redesign is not yet implemented.
- Keep icon/button hit areas large enough to survive larger `UiScale`.
- Treat side panels and dialogs as narrow-reading surfaces; move dense tabular content into stacked cards where possible.

## Areas Still Needing Redesign

These are the places where scaling alone is not a complete answer.

### 1. Portfolio Side-Panel Detail Widgets
Files:
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\PortfolioView.xaml`
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\Controls\CreditCardTxForm.xaml`
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\Controls\AddAssetDialog.xaml`

Current risk:
- The main panel width remains fixed, and some detail fragments still assume a generous horizontal layout.
- Dialogs and side-panel editors introduced recently have not yet gone through the same card/reflow audit as the main lists.

Recommended redesign:
- Audit each dialog/embedded panel and convert long inline rows into stacked field groups before increasing font scale further.
- Prefer narrow-surface cards and grouped summaries over mini-tables.

### 2. Portfolio Overview / Detail Summary Blocks
Files:
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\PortfolioView.xaml`
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\Controls\TxForms\CreditCardTxForm.xaml`

Current risk:
- Several recently added summary strips and field rows inside overlays still assume medium desktop width.
- With larger `UiScale`, some quick-action rows and metric pairs are readable but visually dense.

Recommended redesign:
- Convert remaining fixed `Grid` metric matrices to `WrapPanel` or stacked card groups.
- Treat overlay forms as narrow surfaces and avoid side-by-side label/value pairs unless the content is trivially short.

### 3. Editing / Navigation Surfaces Still Using Hard Width Floors
Files:
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\Controls\TradesTabPanel.xaml`
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Features\Portfolio\Controls\EditTargetsOverlay.xaml`
- `D:\Workspaces\Finances\Assetra\Assetra.WPF\Shell\MainWindow.xaml`

Current risk:
- Some paging/filter footers still rely on explicit `MinWidth` values that assume medium desktop widths.
- A few overlays and shell-level popup surfaces still use fixed widths that remain workable but visually tight at `UiScale = 1.50`.

Recommended redesign:
- Convert footer toolbars to the same wide/compact pattern already used in dialogs and alerts forms.
- Replace overlay fixed widths with adaptive width converters where the surface is intended to float over narrow content.

## Recommended Next Implementation Order

1. Responsive follow-up pass on newer dialogs / side-panel editors
2. Overlay summary card cleanup inside `PortfolioView`
3. Final typography/a11y pass at `UiScale = 1.50`

## Validation Guidance

Each of the following should be checked manually after visual changes:

- `UiScale = 0.85`
- `UiScale = 1.00`
- `UiScale = 1.25`
- `UiScale = 1.50`

Key screens:

- Main shell title/status/nav
- Portfolio tabs:
  - Positions
  - Cash
  - Liability
  - Trades
- Alerts
- Settings
- Add stock dialog
- Fugle help dialog

The target is not “no wrapping ever”; the target is:

- no clipped text
- no unusably tiny controls
- no ambiguous data grouping
- no forced density that hurts readability
