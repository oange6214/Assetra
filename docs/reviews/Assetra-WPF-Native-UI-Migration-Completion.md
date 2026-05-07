# Assetra WPF Native UI Baseline Migration Completion

Last updated: 2026-05-08

This note records the completed native WPF UI baseline migration pass for the
DesignSystem work. It consolidates the temporary migration inventories, audits,
and task lists that established Assetra-owned WPF resources.

This is a baseline completion note, not the final UI quality bar. The next
design-system and page-pattern convergence work is tracked in
`docs/planning/Assetra-Fluent-Carbon-UI-Plan.md`.

## Baseline Scope Completed

- Resource ownership is centralized under `Assetra.WPF/DesignSystem`.
- WPF-UI controls are not used by migrated XAML pages.
- Core styles are owned by the canonical DesignSystem dictionaries:
  buttons, inputs, ComboBox, DatePicker, selection controls, DataGrid, dialogs,
  empty states, page layout, analysis panels, report sections, and tabs.
- Feature pages in the Phase 4 batch consume shared page/dialog/empty-state
  patterns:
  alerts, recurring schedules, financial goals, real estate, insurance
  policies, retirement accounts, physical assets, FIRE, Monte Carlo, asset
  trends, reports, categories, and portfolio subpages.
- Create/edit secondary workflows now use modal dialog or portfolio dialog
  patterns instead of bottom-of-page forms. Categories rule and budget creation
  were moved to centered dialogs to match the rest of the migrated pages.
- Money-like text inputs are covered by `AppMoneyTextBox` or
  `ThousandSeparatorBehavior`; the scanner reports no remaining misses.
- Date fields use `AppDatePicker`; future dates are allowed unless a business
  rule explicitly opts into a constraint.
- ComboBox option display avoids CLR object `ToString()` by using display
  templates, display member paths, or localized option wrappers.

## Known Gaps For The Next Pass

The baseline migration intentionally stopped at resource ownership and broad
page-pattern adoption. The Fluent + Carbon plan continues from here and should
be used for execution.

Known remaining gaps:

- Some pages still use page-local layout choices such as root-level `MaxWidth`,
  centered content, or page-specific `ScrollViewer` structures.
- `StatusBadge`, pagination, filter toolbar, validation text, and helper text
  still need first-class DesignSystem patterns.
- NavRail selected state must use accent/selection semantics, not danger or
  destructive color semantics.
- DataGrid/table-like content must retain visible keyboard focus.
- Motion tokens need real usage guidance and component adoption.

See `docs/planning/Assetra-Fluent-Carbon-UI-Plan.md` for the complete phase
plan and acceptance rules.

## Visual Evidence

The control gallery is rendered in both themes and saved here:

- `docs/reviews/control-gallery-light.png`
- `docs/reviews/control-gallery-dark.png`

Regenerate both screenshots with:

```powershell
.\tools\Capture-ControlGallery.ps1
```

The gallery currently covers:

- primary, secondary, ghost, danger, icon, toolbar, disabled buttons
- text, money, rate, and percent inputs
- ComboBox and DatePicker
- checkbox, radio, and switch states
- top tabs, sub tabs, and segmented controls
- DataGrid
- dialog shell
- centered empty state with primary action

## Page Pattern Decisions

- Use a page header with one right-aligned primary action.
- Loaded lists and tables stretch to the available content area.
- Empty lists center the message vertically and horizontally without decorative
  cards.
- Creation is a dialog when it is a secondary task; it should not remain hidden
  below the list.
- Dense portfolio workflows can keep existing portfolio dialogs when the dialog
  is already a focused transactional surface.
- Reports use first-class report section styles instead of unstructured long
  text blocks.

## Verification

Run this gate after future UI changes. The complete release checklist lives in
`docs/reviews/Assetra-WPF-UI-Release-Gate.md`.

```powershell
.\tools\Scan-XamlResources.ps1 -FailOnExternalBasedOn
.\tools\Scan-MoneyInputs.ps1 -FailOnFinding
.\tools\Capture-ControlGallery.ps1
dotnet build .\Assetra.slnx -c Debug --verbosity minimal
dotnet test .\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-build --verbosity minimal
```

Startup smoke:

```powershell
$exe = Resolve-Path .\Assetra.WPF\bin\Debug\net10.0-windows10.0.19041.0\Assetra.WPF.exe
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 8
if ($p.HasExited) { "Exited early: Code=$($p.ExitCode)" } else { "Started: Id=$($p.Id)"; Stop-Process -Id $p.Id -Force; "Stopped smoke process" }
```

## Future Rule

When a feature page needs a new visual pattern, add the reusable style or
control to `Assetra.WPF/DesignSystem` first, document it in
`Assetra.WPF/DesignSystem/USAGE.md`, and then consume it from the feature page.
