# Assetra WPF UI Release Gate

Last updated: 2026-05-08

Use this gate before releasing a batch that changes `Assetra.WPF/DesignSystem` or major feature-page XAML.

## Required Automated Checks

- [ ] `.\tools\Scan-XamlResources.ps1 -FailOnExternalBasedOn`
- [ ] `.\tools\Scan-MoneyInputs.ps1 -FailOnFinding`
- [ ] `.\tools\Capture-ControlGallery.ps1`
- [ ] `dotnet build .\Assetra.slnx -c Debug --verbosity minimal`
- [ ] WPF startup smoke test
- [ ] `dotnet test .\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-build --verbosity minimal`

## Required Manual Checks

- [ ] ControlGallery in light theme.
- [ ] ControlGallery in dark theme.
- [ ] Theme switch from light to dark and back.
- [ ] Main navigation across all NavRail groups.
- [ ] Portfolio dashboard with data.
- [ ] Portfolio investments with data.
- [ ] Portfolio allocation analysis.
- [ ] Monthly report with statements expanded.
- [ ] Alerts empty state and add dialog.
- [ ] Recurring empty state and add dialog.
- [ ] Goals empty state, list state, add dialog, and delete dialog.
- [ ] FIRE empty result and calculated result.
- [ ] Monte Carlo empty result and calculated result.
- [ ] Real estate empty state, list state, and add dialog.
- [ ] Insurance empty state, list state, and add dialog.
- [ ] Retirement empty state, list state, and add dialog.
- [ ] Physical asset empty state, list state, and add dialog.
- [ ] At least one add/edit dialog.
- [ ] At least one destructive confirmation dialog.
- [ ] At least one DatePicker popup.
- [ ] At least one money input while editing.
- [ ] Tab and Shift+Tab through one list page, one dialog, one report page, and
      one DataGrid page.
- [ ] Escape closes cancellable dialogs.
- [ ] Enter triggers the primary form action only when it is safe for the active
      control.
- [ ] Visual Studio XAML Binding Failures panel has no new failures.

## Required Control States

- [ ] Button: normal, hover, pressed, disabled, focus.
- [ ] Danger button: readable in light and dark theme.
- [ ] TextBox: normal, focus, validation error, disabled.
- [ ] Money TextBox: thousand separators appear while editing.
- [ ] ComboBox: selected item, dropdown hover, dropdown selected, localized display text.
- [ ] DatePicker: field text, today hint, selected day, selected today, future date selection, PastOnly constraint.
- [ ] CheckBox and RadioButton: checked, unchecked, disabled.
- [ ] Tabs: selected, hover, disabled.
- [ ] DataGrid: header, hover row, selected row, horizontal scroll, keyboard
      focus, and visible focused cell/row state.
- [ ] Dialog: overlay opacity, surface contrast, footer buttons, Escape/cancel behavior.
- [ ] EmptyState: centered, readable, one clear primary action when applicable.

## Required Pattern States

- [ ] NavRail: selected item uses accent/selection state, not danger/destructive
      color.
- [ ] Form rhythm: label/input/helper/error/group spacing follows DesignSystem
      tokens.
- [ ] Filter toolbar: search/filter/action controls align and stretch
      predictably.
- [ ] Pagination: current page, previous/next disabled state, and page-size
      selection are readable.
- [ ] Status badges: neutral, success, warning, danger, and muted variants are
      readable in light and dark themes.

## Blockers

- Missing XAML resource.
- `DependencyProperty.UnsetValue` in a style setter.
- App startup crash.
- New binding failure on the active page.
- Dialog overlay allows background content to visually compete with form content.
- NavRail selected state uses a danger/destructive brush.
- ComboBox displays CLR object text.
- DatePicker popup opens without selected-day feedback.
- DataGrid or table-like content cannot be reached or understood by keyboard
  focus.
- Empty state appears inside a decorative card when it is the primary page state.
- Loaded list/table does not stretch to the available area.
- Create/edit form appears as a bottom page panel for a secondary workflow.
