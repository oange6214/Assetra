# Assetra WPF Native UI Task List

Created: 2026-05-07

## Goal

建立一套 Assetra 專屬的 WPF 原生 UI / Design System，逐步取代 WPF-UI 的 shell、resource ownership 與 feature page control usage。

最終狀態：

- Feature views 不直接使用 `ui:` namespace。
- App theme、tokens、control styles、layout patterns 由 `Assetra.WPF/DesignSystem` 統一管理。
- WPF-UI 可以先作為過渡依賴，最後在沒有 runtime dependency 後移除。
- UI 行為一致：按鈕、日期、Tab、Dialog、列表、空狀態、輸入驗證、Light/Dark theme、UI scale。

## Non-goals

- 不一次性重寫所有頁面行為邏輯。
- 不先刪除 WPF-UI package 再修爆掉的 XAML。
- 不把 Tailwind CSS runtime 帶進 WPF。
- 不用每頁散落的 local style 取代另一種混亂。

## Design Principles

- Tailwind-inspired token scale, WPF-native implementation.
- Feature pages depend on Assetra DesignSystem, not third-party controls.
- Prefer control ownership over heavy WPF-UI style overrides.
- Every reusable visual decision lives in `DesignSystem`, not individual feature XAML.
- Page layouts are task-oriented: clear title, actions, content, empty state, form/dialog flow.

## Phase 0 - Inventory and Guardrails

- [ ] Scan all WPF-UI usage:
  ```powershell
  Get-ChildItem Assetra.WPF -Recurse -Filter *.xaml |
      Select-String -Pattern 'xmlns:ui|<ui:|</ui:'
  ```
- [ ] Scan all WPF-UI code references:
  ```powershell
  Get-ChildItem Assetra.WPF -Recurse -Filter *.cs |
      Select-String -Pattern 'Wpf.Ui|ApplicationThemeManager|NavigationView|Snackbar|SymbolIcon'
  ```
- [ ] Produce an inventory table with:
  - File
  - WPF-UI controls used
  - Replacement target
  - Risk level
  - Migration phase
- [ ] Identify current DesignSystem entry points:
  - `Assetra.WPF/App.xaml`
  - `Assetra.WPF/DesignSystem/Styles.xaml`
  - `Assetra.WPF/DesignSystem/Styles/*.xaml`
  - legacy theme/resource files if any
- [ ] Freeze naming rules:
  - Tokens use `Color.*`, `Brush.*`, `Space.*`, `Radius.*`, `Font.*`.
  - Component styles use `App*`, for example `AppPrimaryButton`.
  - Layout styles use `Page*`, `Form*`, `List*`, `Dialog*`.
- [ ] Add a short `DesignSystem/README.md` explaining resource ownership.
- [ ] Decide temporary WPF-UI exceptions:
  - `SymbolIcon` can remain temporarily if no Assetra icon wrapper exists.
  - `NavigationView` can remain until shell replacement phase.

## Phase 1 - Tokens and Themes

Create:

```text
Assetra.WPF/DesignSystem/
  Tokens/
    Colors.xaml
    Spacing.xaml
    Radius.xaml
    Typography.xaml
    Border.xaml
    Shadow.xaml
    Motion.xaml
  Themes/
    Light.xaml
    Dark.xaml
```

- [ ] Define neutral palette tokens inspired by Tailwind slate/zinc scale.
- [ ] Define semantic color tokens:
  - `Brush.Background`
  - `Brush.Surface`
  - `Brush.SurfaceRaised`
  - `Brush.Text.Primary`
  - `Brush.Text.Secondary`
  - `Brush.Text.Muted`
  - `Brush.Border`
  - `Brush.BorderStrong`
  - `Brush.Accent`
  - `Brush.AccentHover`
  - `Brush.Danger`
  - `Brush.Warning`
  - `Brush.Success`
  - `Brush.FocusRing`
- [ ] Define finance-specific colors:
  - `Brush.Up`
  - `Brush.Down`
  - `Brush.Neutral`
  - `Brush.Risk`
  - `Brush.Budget`
- [ ] Define spacing scale:
  - `Space.0`
  - `Space.1`
  - `Space.2`
  - `Space.3`
  - `Space.4`
  - `Space.5`
  - `Space.6`
  - `Space.8`
  - `Space.10`
  - `Space.12`
  - `Space.16`
- [ ] Define reusable `Thickness` resources:
  - `Padding.Button`
  - `Padding.Card`
  - `Padding.Page`
  - `Padding.Dialog`
  - `Margin.Section`
  - `Gap.Form`
- [ ] Define radius scale:
  - `Radius.None`
  - `Radius.Xs`
  - `Radius.Sm`
  - `Radius.Md`
  - `Radius.Lg`
  - `Radius.Full`
- [ ] Define typography:
  - Font family
  - `Font.Size.Xs`
  - `Font.Size.Sm`
  - `Font.Size.Base`
  - `Font.Size.Lg`
  - `Font.Size.Xl`
  - `Font.Size.2xl`
  - weights for regular, medium, semibold
- [ ] Define consistent row/control heights:
  - `Control.Height.Sm`
  - `Control.Height.Md`
  - `Control.Height.Lg`
  - `DataGrid.RowHeight`
  - `DataGrid.HeaderHeight`
- [ ] Merge token dictionaries through one canonical `DesignSystem/Styles.xaml`.
- [ ] Ensure Light/Dark dictionaries own all semantic brushes.
- [ ] Verify theme switching updates only Assetra resources.

Definition of done:

- [ ] App builds.
- [ ] Light/Dark switch works.
- [ ] No missing resource errors.
- [ ] Existing pages still render before control migration starts.

## Phase 2 - Core Controls

Create:

```text
Assetra.WPF/DesignSystem/Controls/
  Button.xaml
  IconButton.xaml
  TextBox.xaml
  NumberTextBox.xaml
  ComboBox.xaml
  CheckBox.xaml
  RadioButton.xaml
  DatePicker.xaml
  Tab.xaml
  Dialog.xaml
  DataGrid.xaml
  EmptyState.xaml
```

### Buttons

- [ ] Build `AppPrimaryButton`.
- [ ] Build `AppSecondaryButton`.
- [ ] Build `AppGhostButton`.
- [ ] Build `AppDangerButton`.
- [ ] Build `AppIconButton`.
- [ ] Build `AppToolbarButton`.
- [ ] Define states:
  - normal
  - hover
  - pressed
  - focused
  - disabled
  - loading if needed
- [ ] Standardize icon + text spacing.
- [ ] Standardize command `CanExecute` disabled visuals.
- [ ] Remove feature-level button color overrides after replacement.

### Text Inputs

- [ ] Build `AppTextBox`.
- [ ] Build validation error state.
- [ ] Build numeric alignment pattern.
- [ ] Build money input behavior with thousand separators.
- [ ] Build rate/percent input pattern.
- [ ] Build clear-button behavior if needed.
- [ ] Ensure IME/CJK input is not broken.

### ComboBox

- [ ] Build `AppComboBox`.
- [ ] Build item selected/hover state.
- [ ] Build disabled state.
- [ ] Support display text options, not object `ToString()`.
- [ ] Replace enum option displays with localized option models.

### DatePicker

- [ ] Build `AppDatePicker` wrapper/control.
- [ ] Display date text as `yyyy-MM-dd`.
- [ ] Do not carry time component in selected date.
- [ ] Show today clearly.
- [ ] Show selected day clearly.
- [ ] Show today + selected day when they are the same.
- [ ] Allow future dates by default.
- [ ] Support optional date constraints per usage:
  - no constraint
  - past-only
  - future-only
  - min/max range
- [ ] Localize month/day labels.
- [ ] Keyboard navigation:
  - arrow keys
  - Enter select
  - Esc close
- [ ] Verify popup contrast in light and dark themes.

### Tabs

- [ ] Build top-level page tab style.
- [ ] Build sub-tab style.
- [ ] Build segmented control style.
- [ ] Ensure selected state is owned by VM commands, not accidental TwoWay group writes.
- [ ] Ensure tab content stretches horizontally and vertically.
- [ ] Replace inconsistent WPF-UI/WPF tab styles.

### Dialogs and Overlays

- [ ] Build modal overlay brush with correct opacity.
- [ ] Build centered dialog shell.
- [ ] Build side panel dialog variant if needed.
- [ ] Build form dialog layout:
  - title
  - subtitle
  - content
  - footer actions
- [ ] Build destructive confirmation dialog.
- [ ] Ensure background content does not visually bleed through.
- [ ] Ensure Esc/cancel behavior is consistent.

### DataGrid and Lists

- [ ] Build `AppDataGrid`.
- [ ] Build `AppDataGridColumnHeader`.
- [ ] Build `AppDataGridRow`.
- [ ] Build row selected/hover state.
- [ ] Build horizontal scroll visual policy.
- [ ] Build empty list placeholder pattern.
- [ ] Build compact card-list alternative for narrow widths if needed.

### Empty State

- [ ] Build `AppEmptyState`.
- [ ] Support:
  - icon
  - title
  - description
  - primary action
  - secondary action optional
- [ ] Empty state must center vertically and horizontally when it is the primary content.
- [ ] Empty state must not be wrapped inside decorative cards unless the page pattern requires it.

Definition of done:

- [ ] A control gallery or sample page exists for all core controls.
- [ ] All core controls render correctly in Light/Dark.
- [ ] Controls have consistent focus visuals.
- [ ] DatePicker solves current selected/today/future-date issues.

## Phase 3 - Layout Patterns

Create:

```text
Assetra.WPF/DesignSystem/Layout/
  Page.xaml
  Form.xaml
  Toolbar.xaml
  List.xaml
  Dashboard.xaml
```

- [ ] Build `PageRootGrid`.
- [ ] Build `PageHeader`.
- [ ] Build `PageTitleRow`.
- [ ] Build `PageActionBar`.
- [ ] Build `PageTabHost`.
- [ ] Build `PageContentHost`.
- [ ] Build `FormGrid`.
- [ ] Build `FormField`.
- [ ] Build `FormSection`.
- [ ] Build `ListContentHost`.
- [ ] Build `DashboardMetricGrid`.
- [ ] Define consistent page padding:
  - standard pages
  - dense portfolio pages
  - dialog pages
- [ ] Define empty vs loaded behavior:
  - Empty: centered message and primary action.
  - Loaded: list/table stretches both directions.
  - Form create/edit appears in dialog/overlay when it is a secondary task.
- [ ] Document when to use inline forms versus dialog forms.

Definition of done:

- [ ] New feature pages can be composed with layout styles without custom margins.
- [ ] Existing simple pages can migrate without local layout hacks.

## Phase 4 - Pilot Migration

Use low-risk pages to validate the new DesignSystem before touching Portfolio.

Pilot pages:

- [ ] FIRE
- [ ] Monte Carlo
- [ ] 財務目標

Tasks:

- [ ] Replace buttons with Assetra button styles.
- [ ] Replace inputs with Assetra input styles.
- [ ] Replace date pickers if present.
- [ ] Replace tabs if present.
- [ ] Replace empty states.
- [ ] Replace dialogs/overlays.
- [ ] Remove direct `ui:` usage where possible.
- [ ] Verify layout with:
  - empty data
  - sample data
  - validation errors
  - Light theme
  - Dark theme
  - 100%, 125%, 150% UI scale

Definition of done:

- [ ] Pilot pages no longer depend on WPF-UI controls except approved temporary exceptions.
- [ ] Visual language is accepted before broader migration.

## Phase 5 - Feature Page Migration

### Batch 1 - Simple Simulation and Planning

- [ ] FIRE
- [ ] Monte Carlo
- [ ] 財務目標

For each page:

- [ ] Use `PageRootGrid`.
- [ ] Use unified page header.
- [ ] Use unified action button placement.
- [ ] Use centered empty state.
- [ ] Use full-stretch list/table when data exists.
- [ ] Move create/edit form into dialog if it is a secondary action.
- [ ] Replace WPF-UI controls with Assetra styles.
- [ ] Verify behavior and build.

### Batch 2 - Multi-asset CRUD Pages

- [ ] 不動產
- [ ] 保險保單
- [ ] 退休專戶
- [ ] 實物資產

For each page:

- [ ] Replace inline create/edit form with consistent dialog flow if appropriate.
- [ ] Keep list/table as primary content.
- [ ] Empty state is centered with primary create action.
- [ ] Localized enum display text is used.
- [ ] Money fields use thousand separators.
- [ ] Date fields display full date and use `AppDatePicker`.
- [ ] Delete confirmation uses `AppDialog`.
- [ ] Loaded list stretches horizontally and vertically.

### Batch 3 - Cashflow and Utility Pages

- [ ] 警示
- [ ] 訂閱排程
- [ ] 收支分類
- [ ] 匯入
- [ ] 對帳

For each page:

- [ ] Replace bottom/inline action forms with consistent dialog/overlay where appropriate.
- [ ] Keep tab content consistent.
- [ ] Ensure action buttons use the same appearance rules.
- [ ] Empty state is centered.
- [ ] Lists/tables stretch when data exists.
- [ ] Pending/triggered state is visually clear.

### Batch 4 - Analytics and Reports

- [ ] 資產趨勢
- [ ] 月結報告
- [ ] 財務總覽

For each page:

- [ ] Use dashboard metric grid patterns.
- [ ] Use readable statement/report sections.
- [ ] Replace hard-to-scan label/value blocks with grouped metric rows.
- [ ] Improve investment performance section readability.
- [ ] Improve risk indicator section readability.
- [ ] Ensure export buttons use consistent style.
- [ ] Ensure charts and tables stretch without awkward whitespace.

### Batch 5 - Portfolio

Portfolio is last because it has the highest interaction density.

- [ ] Main tabs:
  - 儀表板
  - 配置分析
  - 投資
  - 帳戶
  - 負債
  - 交易記錄
- [ ] Replace main tab styles.
- [ ] Replace sub-tab styles.
- [ ] Replace Add Record dialog with Assetra dialog shell.
- [ ] Replace all date pickers in transaction flows.
- [ ] Replace all money fields with unified money input.
- [ ] Replace buy/sell/loan/credit-card transaction forms.
- [ ] Replace allocation overview/rebalance layout.
- [ ] Replace account/liability/position detail panels.
- [ ] Verify:
  - buy
  - sell
  - dividend
  - deposit
  - withdrawal
  - transfer
  - loan borrow
  - loan repayment
  - credit card charge
  - credit card payment
  - edit/revision/delete flows

Definition of done:

- [ ] No feature page has direct `ui:` usage except approved temporary icon wrappers.
- [ ] All page layout behavior is consistent.
- [ ] All create/edit/delete flows use common dialog or page pattern.

## Phase 6 - Shell and Navigation Replacement

- [ ] Build `AppShell`.
- [ ] Build `AppTitleBar`.
- [ ] Build `AppCommandBar`.
- [ ] Build `AppNavRail`.
- [ ] Build nav item selected state.
- [ ] Build nav badge/info indicator.
- [ ] Build collapsed and expanded nav states.
- [ ] Build settings/import footer actions.
- [ ] Replace WPF-UI `NavigationView`.
- [ ] Remove WPF-UI titlebar/backdrop ownership.
- [ ] Ensure theme switching is owned by `AppThemeService`.
- [ ] Verify:
  - startup
  - theme switch
  - navigation selected state
  - legacy child route active state
  - window resize
  - compact nav rail

Definition of done:

- [ ] Shell no longer depends on WPF-UI controls.
- [ ] No white overlay / DWM side effect.
- [ ] Navigation remains accessible and keyboard usable.

## Phase 7 - WPF-UI Removal

- [ ] Run final XAML scan:
  ```powershell
  Get-ChildItem Assetra.WPF -Recurse -Filter *.xaml |
      Select-String -Pattern 'xmlns:ui|<ui:|</ui:'
  ```
- [ ] Run final C# scan:
  ```powershell
  Get-ChildItem Assetra.WPF -Recurse -Filter *.cs |
      Select-String -Pattern 'Wpf.Ui|ApplicationThemeManager|NavigationView|SymbolIcon|CalendarDatePicker'
  ```
- [ ] Replace remaining `SymbolIcon` usage with Assetra icon wrapper or local icon resource.
- [ ] Remove WPF-UI resource dictionaries from `App.xaml`.
- [ ] Remove WPF-UI package reference from central package management.
- [ ] Remove WPF-UI using statements.
- [ ] Remove obsolete WPF-UI workaround comments after they are no longer relevant.
- [ ] Build solution.
- [ ] Run tests.
- [ ] Launch app and verify startup.

Definition of done:

- [ ] No WPF-UI package reference remains.
- [ ] App starts without missing resource errors.
- [ ] Light/Dark theme switching works.
- [ ] Shell and feature pages render correctly.

## Phase 8 - QA, Tests, and Documentation

- [ ] Add UI smoke checklist under `docs/reviews`.
- [ ] Add DesignSystem usage guide.
- [ ] Add control state checklist:
  - normal
  - hover
  - pressed
  - focus
  - disabled
  - validation error
  - dark theme
- [ ] Add page migration checklist template.
- [ ] Add regression tests for VM behavior changed during migration:
  - future transaction dates are allowed where intended
  - date-only normalization
  - money formatting parser still accepts separators
  - tab commands do not leave all tabs inactive
- [ ] Validate at UI scale:
  - 100%
  - 125%
  - 150%
- [ ] Validate app in:
  - Light
  - Dark
  - Chinese
  - English
- [ ] Verify no layout overlap at common desktop widths.
- [ ] Verify dialogs are not overly transparent.
- [ ] Verify empty states are centered.
- [ ] Verify loaded lists/tables stretch.

## Migration Rules

- [ ] Do not convert a page unless the shared style/control it needs already exists.
- [ ] Do not add new page-local button styles.
- [ ] Do not add new page-local tab styles.
- [ ] Do not add new page-local empty state patterns.
- [ ] Do not use `ui:` controls in newly migrated pages.
- [ ] Do not remove WPF-UI package until scans are clean.
- [ ] Keep commits small by phase or page batch.

## Recommended Commit Breakdown

- [ ] `docs(ui): add native WPF UI migration task list`
- [ ] `feat(ui): add Assetra design tokens`
- [ ] `feat(ui): add core button and input styles`
- [ ] `feat(ui): add native date picker style`
- [ ] `feat(ui): add tabs dialogs and empty states`
- [ ] `refactor(ui): migrate FIRE and Monte Carlo pages`
- [ ] `refactor(ui): migrate goals page`
- [ ] `refactor(ui): migrate multi asset pages`
- [ ] `refactor(ui): migrate cashflow utility pages`
- [ ] `refactor(ui): migrate reports and trends pages`
- [ ] `refactor(ui): migrate portfolio pages`
- [ ] `refactor(shell): replace WPF-UI navigation shell`
- [ ] `chore(ui): remove WPF-UI dependency`

## Open Decisions

- [ ] Whether to keep WPF-UI icons temporarily or replace immediately.
- [ ] Whether `AppDatePicker` should be a custom control or a styled WPF `DatePicker`.
- [ ] Whether create/edit forms should always be modal dialogs or sometimes right-side panels.
- [ ] Whether Portfolio dense pages should use a separate compact density token set.
- [ ] Whether the final shell should support compact nav rail labels or icon-only collapse.

## Risks

- [ ] WPF date/calendar control styling may require full template ownership.
- [ ] Removing WPF-UI shell could affect title bar/window chrome behavior.
- [ ] Replacing DataGrid styles can break virtualization or column sizing.
- [ ] Dialog migration can accidentally break keyboard focus and Esc behavior.
- [ ] A large visual migration can hide behavioral regressions without smoke tests.

## Final Definition of Done

- [ ] `dotnet build` succeeds.
- [ ] Tests pass.
- [ ] App launches cleanly.
- [ ] No `ui:` namespace remains in feature views.
- [ ] No WPF-UI package reference remains.
- [ ] All pages use Assetra DesignSystem styles.
- [ ] All date pickers clearly show today, selected day, and allow future dates unless constrained.
- [ ] All money inputs display thousand separators.
- [ ] All major pages have consistent empty and loaded states.
- [ ] Light/Dark themes are fully owned by Assetra resources.
- [ ] Documentation explains how to build future UI without reintroducing WPF-UI ownership.
