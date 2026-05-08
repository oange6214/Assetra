# Assetra Fluent + Carbon UI Plan

Last updated: 2026-05-08

This document defines the next UI direction for Assetra WPF after the native
DesignSystem migration. Assetra should use Fluent Design as the primary visual
and interaction language, with Carbon Design System used only as a secondary
reference for data-heavy product workflows.

## Decision

Assetra is a Windows finance application. Its UI should feel native to Windows,
calm, precise, and dependable. The design system should therefore be:

- Fluent-first for visual language, control behavior, accessibility, motion,
  shell, dialogs, focus states, theme behavior, and native Windows ergonomics.
- Carbon-assisted for information architecture in dense finance screens:
  data tables, filter bars, empty states, metric groupings, report sections,
  list/detail patterns, and enterprise workflow clarity.
- Assetra-owned for tokens, components, resource names, and implementation.
  Carbon and Fluent are references, not runtime dependencies.

## Why This Direction Fits Assetra

Fluent fits the platform expectation. Assetra is a WPF desktop application, so
native keyboard behavior, focus visuals, shell integration, accessibility, and
theme switching matter more than web-style visual novelty.

Carbon fits the product complexity. Assetra has many operational screens:
portfolio holdings, allocation analysis, monthly reports, alerts, recurring
schedules, categories, goals, and multi-asset records. These screens need clear
table density, predictable filters, empty states, and readable financial data.

The combined rule is simple:

> Fluent decides how the product feels. Carbon helps decide how dense product
> information is organized.

## Current Project Findings

Checked areas:

- `Assetra.WPF/DesignSystem`
- `Assetra.WPF/Features`
- `docs/reviews/Assetra-WPF-Native-UI-Migration-Completion.md`
- `docs/reviews/Assetra-WPF-UI-Release-Gate.md`
- `Assetra.WPF/DesignSystem/README.md`
- `Assetra.WPF/DesignSystem/USAGE.md`

Current state:

- Resource ownership is already centralized under `Assetra.WPF/DesignSystem`.
- The DesignSystem already has tokens, themes, styles, controls, empty states,
  dialogs, data grid styles, inputs, tabs, and page layout styles.
- The release gate already tracks startup crashes, binding failures, DatePicker
  behavior, money inputs, dialog contrast, empty states, list/table stretch, and
  create/edit form placement.
- Feature XAML already consumes many shared resources such as
  `AppPrimaryButton`, `AppDataGrid`, and `AppDialogShell`.
- Token primitives were recently consolidated to Tailwind v4 hex values in
  `Tokens/Colors.xaml`, and light/dark themes now reference those primitives
  instead of hard-coding hex values. This gives the Fluent/Carbon-aligned token
  work a cleaner foundation without making Tailwind a runtime or design-system
  dependency.

Remaining risk:

- Some pages still use local layout choices such as page-level `MaxWidth`,
  centered content, or page-specific `ScrollViewer` structures.
- Some pages can still look visually unrelated because data layout and page
  layout are not governed by one documented pattern.
- Pagination, status badge, and filter toolbar patterns are not yet first-class
  components, so feature pages reinvent them locally.

## Design Ownership

### Fluent Owns

- App shell, title bar, navigation rail, toolbar controls, and window behavior.
- Button, input, ComboBox, DatePicker, CheckBox, RadioButton, Tab, DataGrid,
  Expander, and Dialog control states.
- Focus visuals, keyboard interaction, hover/pressed/disabled states, and
  accessibility contrast.
- Theme behavior, semantic brushes, typography, icon treatment, radius, shadow,
  and motion.
- Native Windows expectations such as keyboard tab order, Escape/cancel
  behavior, selected state clarity, and readable disabled states.

### Carbon Assists

- Data table structure and density for financial rows.
- Filter toolbar patterns: search, date range, type filter, asset filter,
  pagination, and batch actions.
- Empty state hierarchy: icon, title, description, one primary action.
- Enterprise workflow clarity: list first, then secondary create/edit dialog or
  side panel.
- Report section structure: metric grids, grouped sections, readable exports,
  and comparison rows.
- Dashboard and analysis information grouping, especially when the page is data
  dense rather than decorative.

### Assetra Owns

- All XAML resources and naming.
- All app-specific tokens and semantic brushes.
- All finance-specific interaction rules.
- All localization behavior.
- All release gates and visual QA requirements.

## Non-Goals

- Do not add Carbon as a runtime dependency.
- Do not add WPF-UI back as a visual dependency.
- Do not copy Carbon's exact visual style, black/gray palette, or web component
  spacing into the WPF app.
- Do not build a Tailwind-like utility class system in XAML.
- Do not allow each feature page to define its own buttons, tabs, empty states,
  dialogs, or input styles.

## Canonical Page Patterns

### Standard List Page

Use for alerts, categories, recurring schedules, goals, multi-asset pages, and
similar CRUD screens.

Structure:

1. `PageRootGrid`
2. `PageHeaderDock`
3. `PageTitleRow`
4. Right-aligned `PageActionBar`
5. Optional `ListFilterBar`
6. Loaded state using `ListPageLoadedHost` or `AppDataGrid`
7. Empty state using `ListPageEmptyHost` and `AppEmptyState`
8. Create/edit flow using `AppDialogShell` or `AppSidePanelDialogShell`

Rules:

- Loaded lists and tables stretch horizontally and vertically.
- Empty states are centered horizontally and vertically without decorative
  cards.
- Create/edit forms should not be hidden at the bottom of the page.

### Data Table Page

Use for transactions, reports, alerts with many rows, and dense asset records.

Rules:

- Use `AppDataGrid` for tabular data.
- Keep financial numbers aligned and readable.
- Allow horizontal scroll for dense financial tables.
- Do not compress columns until values become unreadable.
- Use filter toolbar before the table, not scattered controls.

### Analysis Page

Use for FIRE, Monte Carlo, allocation analysis, and other calculator-like
screens.

Structure:

1. `AnalysisInputPanel`
2. Primary action aligned with the input group
3. `AnalysisResultHost` when calculated
4. `AnalysisEmptyHost` when no result is available

Rules:

- Inputs should not float in a narrow centered column unless the task is truly
  narrow.
- Result panels should stretch to the available content area.
- Empty result states should explain the next action.

### Report Page

Use for monthly report and exportable statements.

Rules:

- Use `ReportToolbar`, `ReportSection`, `ReportChartSection`,
  `ReportTableSection`, and `ReportExportBar`.
- Avoid long unstructured text blocks.
- Group financial figures into readable rows or metric grids.
- Export actions belong to the section they export.

### Dialog Flow

Use for secondary create/edit tasks.

Rules:

- Use `AppDialogOverlay` and `AppDialogShell`.
- Overlay must visually separate foreground form from background content.
- Footer actions are right-aligned: secondary first, primary last.
- Destructive confirmation uses `AppDestructiveDialogShell` and
  `AppDangerButton`.
- Dense workflows may use `AppSidePanelDialogShell` when keeping context visible
  helps the user.

### Form Vertical Rhythm

Use Carbon's form discipline as the reference for spacing inside forms, while
keeping Fluent control visuals.

Rules:

- Label to input: `Gap.Sm` / 8 px.
- Input to helper text: `Gap.Xs` / 4 px.
- Input to error text: `Gap.Xs` / 4 px.
- Field to field in the same group: `Gap.Sm` / 8 px.
- Form group to form group: `Gap.Xl` / 24 px so the group boundary stays
  visually obvious.
- Dialog body to footer: `Gap.Xl` / 24 px unless the dialog is intentionally
  compact.
- Helper text should be secondary text; validation error should use the danger
  semantic brush and remain close to the field it describes.

## Token Direction

Keep the current token structure:

- Primitive tokens: `Color.*`, `Space.*`, `Radius.*`, `Font.*`, `Control.*`,
  `Border.*`, `Shadow.*`, and `Motion.*`.
- Semantic brushes: `Brush.*`.
- Backward-compatible aliases: `AppBackground`, `AppSurface`, `AppAccent`, and
  related names while migration continues.

Add or clarify these token groups before major page consolidation begins:

- Gap tokens: `Gap.Xs`, `Gap.Sm`, `Gap.Md`, `Gap.Lg`, `Gap.Xl`.
- Line height tokens: `LineHeight.Tight`, `LineHeight.Normal`,
  `LineHeight.Relaxed`.
- Motion easing tokens: `Motion.Easing.Standard`, `Motion.Easing.Enter`,
  `Motion.Easing.Exit`.
- Composite text styles: `TextStyle.Heading.*`, `TextStyle.Body.*`,
  `TextStyle.Caption.*`.
- Data density: table row height, table header height, compact row padding.
- Toolbar density: search height, filter gap, command button spacing.
- Page rhythm: standard content margin, section gap, title/action spacing.
- Dialog sizing: compact, standard, dense side panel.

## Implementation Phases

### Phase 0: Documentation Lock

- [x] Add this plan to `docs/INDEX.md`. *(Done: linked under Planning.)*
- [x] Update `Assetra.WPF/DesignSystem/README.md` with Fluent-first /
      Carbon-assisted ownership. *(Done: Design Direction section added.)*
- [x] Update `Assetra.WPF/DesignSystem/USAGE.md` with page pattern examples.
      *(Done: Design Direction, Form Rhythm, and Shared Product Patterns added.)*
- [x] Update `docs/reviews/Assetra-WPF-UI-Release-Gate.md` with the new
      Fluent/Carbon acceptance rules. *(Done: Required Pattern States section,
      keyboard navigation checks, and NavRail/DataGrid blockers added.)*

### Phase 1: DesignSystem Hardening + Known Issues

- [ ] Review `Tokens/` naming for semantic clarity.
- [ ] Confirm light/dark theme brushes cover all control states.
- [ ] Confirm focus visuals are visible and consistent.
- [ ] Confirm DatePicker selected day, today hint, and future date behavior.
- [ ] Confirm button foreground and disabled contrast in both themes.
- [ ] Confirm dialog overlay opacity prevents background visual competition.
- [x] Replace the NavRail active indicator's current danger/destructive brush
      with the accent/selection semantic brush. The selected navigation marker
      should not use `AppDanger`. *(Done in commit 92dcbb0.)*
- [x] Restore keyboard focus behavior for `AppDataGrid` cells and rows. A
      `FocusVisualStyle="{x:Null}"` default conflicts with Fluent native
      Windows expectations when Tab/Shift+Tab navigation reaches tabular data.
      *(Done in commit 92dcbb0: AppDataGridCell uses `{DynamicResource FocusVisual}`.)*
- [x] Wire existing motion tokens into at least one real control state or
      document that motion is intentionally disabled for a specific component.
      *(Done in commit 92dcbb0: NavRail collapse animates with Motion.Fast +
      Motion.Easing.Standard.)*

### Phase 1.5: Token Additions

- [x] Add `Gap.Xs`, `Gap.Sm`, `Gap.Md`, `Gap.Lg`, and `Gap.Xl` tokens and map
      them to the canonical form/page rhythm. *(Done in commit 10698a5.)*
- [x] Add `LineHeight.Tight`, `LineHeight.Normal`, and `LineHeight.Relaxed`.
      *(Done in commit 10698a5.)*
- [x] Add `Motion.Easing.Standard`, `Motion.Easing.Enter`, and
      `Motion.Easing.Exit`. *(Done in commit 10698a5: CubicEase + Spline KeySpline counterparts.)*
- [x] Add composite text style resources for headings, body text, and
      captions. *(Done in commit 10698a5: TextStyle.Heading.{Lg,Md,Sm}, Body.{Lg,Md,Sm,Strong.Md}, Caption.)*
- [x] Add a tabular numeric text style (`TextStyle.Numeric` or
      `TextStyle.Body.Numeric`) with `FontFeatureSettings` for tabular figures
      where the font fallback chain supports them. Required for column-aligned
      financial values in tables and metric grids. *(Done in commit 10698a5:
      TextStyle.Numeric.{Display,Body,Caption} with Typography.NumeralAlignment=Tabular.)*
- [ ] Replace recurring local spacing literals with token references during
      page migration instead of adding new one-off margins. *(Open: ~29
      candidate `<ColumnDefinition Width="N">` / `<RowDefinition Height="N">`
      sites identified across feature pages where N matches a Gap token.
      Deferred to per-page migration in Phase 3-6 to keep diffs focused.)*

### Phase 2: Layout Foundation

- [x] Audit feature pages for page-level `MaxWidth`. *(Done: no page-level
      `MaxWidth` violations. All `MaxWidth` instances in Categories, Goals,
      Recurring, Settings are inner-form readability constraints, not page roots.)*
- [x] Audit feature pages for root-level `HorizontalAlignment="Center"`.
      *(Done: zero matches across Features/.)*
- [x] Audit feature pages for root-level `VerticalAlignment="Center"`.
      *(Done: zero matches across Features/.)*
- [x] Replace page-local layout wrappers with canonical page hosts. *(Done as
      part of v0.22-v0.23 native UI migration: 12/12 feature pages use
      `PageRootGrid` + `PageHeaderDock`/`ListPageHeader` + `AppDialogShell`.)*
- [x] Ensure tabs stretch selected content horizontally and vertically. *(Done
      via `PageTabHost` style in PageLayout.xaml.)*
- [x] Ensure loaded list/table content stretches to available space. *(Done:
      8 pages consume `AppDataGrid`; lists use `ListContentHost` for vertical
      stretch.)*

### Phase 2.5: Component Foundations

These components are required by the feature-page migrations in Phase 3-6.
Build them before page migration so each page consumes the canonical version
instead of inventing a local one that has to be replaced later.

- [x] Add or standardize `StatusBadge` for state labels such as active,
      archived, pending, healthy, warning, and destructive states.
      *(Done in commit 33c92ec: Styles/Badges.xaml with 8 variants.)*
- [x] Add or standardize pagination controls for transaction/report-style
      lists. *(Done in commit 33c92ec: Styles/Pagination.xaml + PaginationNavButton variant.)*
- [x] Add a first-class filter toolbar pattern for search, date range, type
      filter, asset filter, and right-aligned actions.
      *(Done in commit 33c92ec: Styles/FilterToolbar.xaml.)*
- [x] Add reusable validation text and helper text patterns that follow the
      Form Vertical Rhythm rules. *(Done in commit 33c92ec: Styles/FormText.xaml
      with Form.{FieldLabel,HelperText,ErrorText,SectionHeader,SectionGap}.)*

### Phase 3: Portfolio Pages

- [ ] Portfolio dashboard: remove unwanted horizontal compression.
- [ ] Portfolio investments: keep list and filter region full-width.
- [ ] Allocation analysis: prevent layout switching/flicker and use one stable
      responsive layout.
- [ ] Accounts and liabilities: align metric cards, filter bars, and tables.
- [ ] Trades: ensure filter toolbar, list, and pagination stretch consistently.

### Phase 4: Planning and Income Pages

- [ ] Categories: keep category/rule/budget tabs visually consistent.
- [ ] Categories: create/edit category flow uses the same dialog pattern as
      other CRUD pages.
- [ ] Recurring: both tabs use consistent empty states and action placement.
- [ ] Goals: list, empty state, add dialog, edit, and delete confirmation follow
      the shared dialog pattern.
- [ ] Alerts: list, empty state, add dialog, and toolbar use the shared pattern.

### Phase 5: Reports and Trends

- [ ] Monthly report: convert investment performance and risk indicators into
      readable metric/table sections.
- [ ] Monthly report: avoid long unstructured text blocks.
- [ ] Asset trends: use a stable full-width analysis/report layout.
- [ ] Export buttons remain section-scoped and readable.

### Phase 6: Multi-Asset and Simulation Pages

- [x] Real estate: list stretches when data exists; empty state is centered.
- [x] Insurance: list stretches when data exists; empty state is centered.
- [x] Retirement: list stretches when data exists; empty state is centered.
- [x] Physical assets: list stretches when data exists; empty state is centered.
- [ ] FIRE: inputs and result states follow analysis page pattern.
- [ ] Monte Carlo: inputs and result states follow analysis page pattern.

### Phase 7: QA and Release Gate

- [x] Run `tools/Scan-XamlResources.ps1 -FailOnExternalBasedOn`. *(Pass: all
      BasedOn StaticResource references local to their dictionary.)*
- [x] Run `tools/Scan-MoneyInputs.ps1 -FailOnFinding`. *(Pass: no money-like
      bindings missing thousand-separator behavior.)*
- [ ] Run `tools/Capture-ControlGallery.ps1`. *(Pending: requires running app.)*
- [x] Build `Assetra.slnx`. *(Pass: 0 warnings, 0 errors.)*
- [x] Run `Assetra.Tests`. *(Pass: 1161/1161.)*
- [ ] Smoke test app startup.
- [ ] Check Visual Studio XAML Binding Failures panel.
- [ ] Manually verify all release gate pages in light and dark themes.
- [ ] Verify Tab and Shift+Tab navigation order on at least one list page, one
      dialog, one report page, and one DataGrid page.
- [ ] Verify Escape closes cancellable dialogs.
- [ ] Verify Enter triggers the primary form action only when doing so is safe
      and consistent with the active control.

### Phase 8: Component Polish

After feature-page migration is complete, finish the design system with motion
adoption and visual regression coverage. Component primitives themselves were
delivered in Phase 2.5; this phase wires them up across the gallery and adds
motion behavior.

- [x] Add motion usage guidance and consume motion tokens where transitions
      improve clarity without making finance workflows feel noisy.
      *(Done: DesignSystem/USAGE.md "Motion" section documents tokens, when
      to use, when NOT to use, and the NavRail collapse reference
      implementation. NavRail is the first wired consumer.)*
- [x] Extend ControlGallery to cover badges, pagination, validation states,
      filter toolbar, and motion-capable controls. *(Done: ControlGallery.xaml
      now showcases StatusBadge.{Neutral,Success,Warning,Danger,Info,Special,
      Accent,Muted}, Pagination layout, Form Vertical Rhythm with helper +
      error states, and FilterToolbar pattern. NavRail collapse animation is
      visible in the live shell.)*

## Acceptance Rules

A page is considered migrated only when:

- It uses shared DesignSystem resources for buttons, inputs, tabs, dialogs,
  empty states, and data grids.
- It uses shared DesignSystem resources for status badges, pagination, filter
  toolbars, validation text, and helper text when those patterns appear.
- It has no page-local visual style for common controls.
- Loaded list/table content stretches horizontally and vertically.
- Empty state content is centered horizontally and vertically.
- Primary action location is predictable.
- Create/edit secondary workflows use dialog or side panel patterns.
- Date fields show the selected date clearly.
- Money-like inputs show thousand separators.
- ComboBox selected values and dropdown items show localized display text.
- Tab and Shift+Tab navigate through interactive controls in visual order.
- Escape closes cancellable dialogs.
- Enter triggers the primary form action when the active control does not need
  Enter for text editing or selection.
- Light and dark themes both remain readable.
- No new XAML binding failures appear on the active page.

## Documentation Updates Required After Each Batch

- Update `docs/reviews/Assetra-WPF-Native-UI-Migration-Completion.md` when a
  phase is completed.
- Update `docs/reviews/Assetra-WPF-UI-Release-Gate.md` when a new required
  manual check is discovered.
- Update `Assetra.WPF/DesignSystem/USAGE.md` when a new reusable pattern is
  introduced.
- Update `docs/releases/CHANGELOG.md` before release.

## Reference Links

- [Fluent 2 Design Principles](https://fluent2.microsoft.design/design-principles)
- [Windows Design Principles](https://learn.microsoft.com/windows/apps/design/design-principles)
- [Carbon Data Table Usage](https://carbondesignsystem.com/components/data-table/usage/)
- [Carbon Empty States Pattern](https://carbondesignsystem.com/patterns/empty-states-pattern/)
