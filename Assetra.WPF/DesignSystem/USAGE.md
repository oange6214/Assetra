# Assetra WPF DesignSystem Usage Guide

This guide defines the default UI patterns for Assetra WPF feature pages. New UI should use these resources first, and only add a new shared style when the current system cannot express the required pattern.

## Design Direction

Assetra's UI is Fluent-first and Carbon-assisted:

- Fluent owns the Windows-native visual language, control states, focus,
  keyboard behavior, dialogs, theme behavior, and accessibility.
- Carbon assists dense finance workflows: data tables, filter toolbars, empty
  states, list/detail structure, report sections, and form rhythm.
- Assetra owns the XAML resources. Fluent and Carbon are references, not runtime
  dependencies.

The full execution plan lives in
`docs/planning/Assetra-Fluent-Carbon-UI-Plan.md`.

## Resource Flow

1. Primitive tokens live in `DesignSystem/Tokens`.
2. Light and dark semantic brushes live in `DesignSystem/Themes`.
3. Reusable layout and style families live in `DesignSystem/Styles`.
4. Control-specific templates and variants live in `DesignSystem/Controls`.
5. Feature XAML consumes those styles and should avoid page-local visual styles.

`Styles.xaml` is the application-level registry. It should only merge dictionaries. Do not put style implementations there.

Compatibility shims under `DesignSystem/Controls` may import a canonical `DesignSystem/Styles` file for old pack URIs, but new work should edit the canonical file documented in `README.md`.

## Page Layout

- Use `PageRootGrid` as the outer page chrome.
- Use `PageHeaderDock`, `PageTitleRow`, `PageTitleText`, and `PageActionBar` for page headers.
- Use `PageContentHost` when the page contains a mix of panels.
- Use `ListContentHost` for loaded lists that should stretch vertically.
- Use `PageTabHost` around tab content so selected pages fill the available area.
- Use `DashboardMetricGrid`, `MetricCard`, `MetricLabel`, and `MetricValue` for KPI rows.

## Forms

- Use `FormSection` for grouped form content.
- Use `PageFormGrid`, `FormField`, `FormFieldLast`, and `FormFieldLabel` for simple aligned fields.
- Use `AppTextBox` for normal text.
- Use `AppMoneyTextBox` with `ThousandSeparatorBehavior` for currency and money-like inputs.
- Use `AppRateTextBox` for decimal rate inputs such as `0.05`.
- Use `AppPercentTextBox` for percent inputs such as `5`.
- Use `AppComboBox` for dropdowns. Option objects should expose `DisplayName`, `Display`, `Label`, `Name`, or `Title`.
- Use `AppDatePicker` for dates. Set `DatePickerDateOnlyBehavior.Constraint` only when the business rule requires `PastOnly`, `FutureOnly`, or `Range`.

### Form Rhythm

Use the shared gap tokens from the Fluent + Carbon plan as they are added:

- Label to input: `Gap.Sm` / 8 px.
- Input to helper text: `Gap.Xs` / 4 px.
- Input to error text: `Gap.Xs` / 4 px.
- Field to field in the same group: `Gap.Sm` / 8 px.
- Form group to form group: `Gap.Xl` / 24 px.
- Dialog body to footer: `Gap.Xl` / 24 px unless intentionally compact.

Helper text should use secondary text. Error text should use the danger
semantic brush and stay close to the field it describes.

## Buttons

- Use `AppPrimaryButton` for the main page or dialog action.
- Use `AppSecondaryButton` for secondary actions.
- Use `AppGhostButton` for low-emphasis inline actions.
- Use `AppDangerButton` for destructive confirmation.
- Use `AppIconButton` for icon-only row actions.
- Use `AppToolbarButton` for shell or toolbar icon buttons.
- Use `AppEmptyStatePrimaryAction` and `AppEmptyStateSecondaryAction` inside shared empty states.
- For icon + text buttons, use a horizontal `StackPanel` with `AppButtonContent`,
  `AppButtonLeadingIcon`, and `AppButtonText`. This keeps button labels and
  icons readable in both light and dark themes even though the app has a global
  implicit `TextBlock` style.

## Empty States

- Empty state content should be centered horizontally and vertically when it is the primary page content.
- Do not wrap the empty state in a decorative card unless it is part of a larger panel.
- Use `AppEmptyState`, `EmptyStateIcon`, `AppEmptyStateTitle`, `AppEmptyStateDescription`, and `AppEmptyStateActionBar`.
- Include one primary action when the user can create the first item.

## Dialogs

- Use `AppDialogOverlay` for modal scrims.
- Use `AppDialogShell` for centered form dialogs.
- Use `AppDestructiveDialogShell` for destructive confirmation.
- Use `AppSidePanelDialogShell` only for dense secondary workflows that benefit from keeping the list visible.
- Dialog content should be structured with `AppDialogHeader`, `AppDialogTitle`, `AppDialogSubtitle`, `AppDialogBody`, and `AppDialogFooter`.
- Dialog overlays must fully separate background content from foreground form content.

## Lists and Tables

### Standard List Page Hosts

Use these hosts when a feature page is a CRUD list (alerts, categories, recurring schedules, goals, multi-asset pages, etc.). They match the "Standard List Page" pattern in `docs/planning/Assetra-Fluent-Carbon-UI-Plan.md`.

- Use `ListPageHeader` for the page header row (DockPanel based on `PageHeaderDock`).
- Use `ListPageActionBar` for the right-aligned primary action area.
- Use `ListFilterBar` for the optional filter/search bar above the list.
- Use `ListPageLoadedHost` as the container for the loaded data state.
- Use `ListPageEmptyHost` as the container for the empty state.
- Use `ListContentHost` only as the inner `ScrollViewer` style when content needs to stretch vertically inside one of the hosts above.

### Tables

- Use `AppDataGrid` for tabular data.
- Loaded lists and tables should stretch horizontally and vertically.
- Empty lists should show the shared empty state inside `ListPageEmptyHost` instead of a blank table.
- Horizontal scroll is allowed for dense financial tables; do not compress columns until values become unreadable.

## Shared Product Patterns

The Fluent + Carbon plan requires these patterns to become first-class
DesignSystem resources before broad page migration:

- `StatusBadge` for active, archived, pending, healthy, warning, and
  destructive states.
- Pagination for transaction/report-style lists.
- Filter toolbar for search, date range, type filter, asset filter, and
  right-aligned actions.
- Validation text and helper text that follow the form rhythm above.

Until a pattern exists, add it to DesignSystem first, document it here, and then
consume it from feature pages. Do not create a page-local implementation that
will have to be replaced later.

## Reports and Analysis

- Use `AnalysisInputPanel`, `AnalysisResultHost`, and `AnalysisEmptyHost` for calculator-style pages.
- Use `ReportToolbar`, `ReportSection`, `ReportChartSection`, `ReportTableSection`, and `ReportExportBar` for monthly report sections.
- Report sections should group financial figures into readable grids instead of long unstructured text blocks.

## Motion

Motion is calm and brief in finance UI: every transition should feel like
acknowledgement, not entertainment. Reach for animation only when the
movement makes a state change easier to follow.

### Tokens

- `Motion.Fast` (120 ms) — small, immediate feedback (focus rings, hover
  brightening, selection acknowledgement, NavRail pane collapse).
- `Motion.Normal` (180 ms) — most layout changes (drawer / dialog open
  and close, expander toggle).
- `Motion.Slow` (240 ms) — deliberate emphasis (overlay scrim fade-in,
  full-page transition).
- `Motion.Easing.Standard` — default decelerating curve. Use for the
  overwhelming majority of transitions.
- `Motion.Easing.Enter` — entry / expand transitions where content slides
  or fades into view.
- `Motion.Easing.Exit` — exit / collapse transitions where content slides
  out or fades away.
- `Motion.Easing.*.Spline` — `KeySpline` counterparts of the easings
  above for `SplineDoubleKeyFrame.KeySpline` consumers.

### When to use motion

- Use motion for state changes that the user is already paying attention
  to: a panel opening, a dialog appearing, a list collapsing, an item
  being added or removed.
- Use motion to make a non-obvious change visible: tab switching where
  the new tab content can fade in over `Motion.Fast`, expander
  expanding height with `Motion.Normal`.

### When NOT to use motion

- Repeated background updates: portfolio tickers, table cell value
  refreshes, P/L numbers ticking — these should not animate. The user is
  monitoring values; motion adds noise.
- DataGrid row selection / hover: keep instant. Hover delay or animated
  selection makes tables feel laggy under keyboard navigation.
- Chart redraws: chart libraries already have their own motion; do not
  layer additional animation on top.
- Anything triggered by polling or background sync: no animation on
  arrival.

### Reference: NavRail collapse

The reference implementation lives in `Shell/NavRailView.xaml`. The
NavPane width animates between `Size.Sidebar.Width` and `56` over
`Motion.Fast` with `Motion.Easing.Standard`, using
`DataTrigger.EnterActions` and `DataTrigger.ExitActions` Storyboards
with `FillBehavior=HoldEnd`. Replicate this pattern for any other pane
toggle.

## Do Not

- Do not introduce new `ui:` controls.
- Do not define page-local button, tab, empty-state, dialog, or input styles.
- Do not define page-local status badge, pagination, filter toolbar, validation,
  or helper text styles.
- Do not use object `ToString()` as ComboBox display text.
- Do not hide create/edit forms at the bottom of pages when the form is a secondary task; use a dialog or side panel.
- Do not put raw `TextBlock` content inside a primary or danger button without
  `AppButtonText` or an explicit foreground binding.
