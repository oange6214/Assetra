# Wpf.Ui Full Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all remaining standard WPF controls with Wpf.Ui equivalents, and remove custom Button styles (BtnPrimary, BtnGhost, BtnIcon, BtnIconToggle) in favour of `ui:Button Appearance="..."`.

**Architecture:** Module-first batch migration (Batch 1→5). Each batch migrates all control types in a feature module at once. GlobalStyles.xaml custom Button style definitions are removed in the final batch, after all per-file references have been cleared. Standard WPF `DataGrid` is kept but globally styled via GlobalStyles.xaml.

**Tech Stack:** .NET 10 / WPF / Wpf.Ui 4.2.0 / C# / XAML

---

## Transformation Reference

These rules apply throughout all tasks. Always check them before editing a file.

### R1 — xmlns:ui namespace
Every modified XAML file must declare:
```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```
Add it to the root element's namespace declarations if missing.

### R2 — Direct tag substitutions
| Before | After |
|--------|-------|
| `<Button` / `</Button>` | `<ui:Button` / `</ui:Button>` |
| `<ComboBox` / `</ComboBox>` | `<ui:ComboBox` / `</ui:ComboBox>` |
| `<RadioButton` / `</RadioButton>` | `<ui:RadioButton` / `</ui:RadioButton>` |
| `<ListBox` / `</ListBox>` | `<ui:ListBox` / `</ui:ListBox>` |
| `<ListBoxItem` / `</ListBoxItem>` | `<ui:ListBoxItem` / `</ui:ListBoxItem>` |
| `<CheckBox` / `</CheckBox>` | `<ui:CheckBox` / `</ui:CheckBox>` |

Self-closing forms follow the same rule: `<Button ... />` → `<ui:Button ... />`.

### R3 — Button style → Appearance attribute
When a `<Button>` carries one of the custom styles, replace **both** the tag and the style:

| Style attribute | New tag + attribute |
|----------------|---------------------|
| `Style="{StaticResource BtnPrimary}"` | `<ui:Button Appearance="Primary"` (remove Style attr) |
| `Style="{StaticResource BtnGhost}"` | `<ui:Button Appearance="Secondary"` (remove Style attr) |
| `Style="{StaticResource BtnIcon}"` | `<ui:Button Appearance="Transparent"` (remove Style attr) |
| `Style="{StaticResource BtnIconToggle}"` | Keep `<ToggleButton`, **remove** the Style attribute only |

Example — BtnIcon:
```xml
<!-- Before -->
<Button Style="{StaticResource BtnIcon}" Command="{Binding EditCommand}">
    <ui:SymbolIcon Symbol="Edit24" />
</Button>

<!-- After -->
<ui:Button Appearance="Transparent" Command="{Binding EditCommand}">
    <ui:SymbolIcon Symbol="Edit24" />
</ui:Button>
```

Example — BtnPrimary:
```xml
<!-- Before -->
<Button Style="{StaticResource BtnPrimary}" Content="儲存" Command="{Binding SaveCommand}" />

<!-- After -->
<ui:Button Appearance="Primary" Content="儲存" Command="{Binding SaveCommand}" />
```

### R4 — Expander judgment
- Has a meaningful `Header` string + wraps a content area (form, list, settings panel) → replace with `ui:CardExpander`
- Pure collapse/expand with no semantic meaning → keep `<Expander>`, ControlsDictionary will style it

```xml
<!-- Semantic: replace -->
<Expander Header="進階設定">
    <StackPanel> ... </StackPanel>
</Expander>
<!-- becomes -->
<ui:CardExpander Header="進階設定">
    <StackPanel> ... </StackPanel>
</ui:CardExpander>
```

### R5 — Border judgment
Replace with `ui:Card` **only** when ALL three conditions hold:
1. Has `Padding` attribute (content container, not decorative)
2. Contains multiple child elements (not a single-child clip wrapper)
3. Not used as clip mask or animation target (`x:Name` used in storyboard)

```xml
<!-- Before: semantic container → replace -->
<Border Padding="16" Background="{DynamicResource AppSurface}" CornerRadius="8">
    <StackPanel> ... </StackPanel>
</Border>
<!-- After -->
<ui:Card Padding="16">
    <StackPanel> ... </StackPanel>
</ui:Card>

<!-- Single-child clip wrapper → keep as Border -->
<Border CornerRadius="8" ClipToBounds="True">
    <Image Source="{Binding Logo}" />
</Border>
```

### R6 — ScrollViewer
ControlsDictionary automatically styles `ScrollViewer`. No tag change needed unless the file has a specific Wpf.Ui scroll integration requirement. Keep as `<ScrollViewer>`.

### R7 — DataGrid (Batch 5 only)
DataGrid is styled globally — no per-file tag changes. See Task 10.

---

## Batch 1 — Portfolio

### Task 1: Portfolio/Controls — TxForms (10 files)

**Files to modify:**
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml` — ComboBox, RadioButton, ListBox, CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/CashDividendTxForm.xaml` — ComboBox, RadioButton, CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/CashFlowTxForm.xaml` — ComboBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/CreditCardTxForm.xaml` — ComboBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/IncomeTxForm.xaml` — ComboBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/LoanTxForm.xaml` — ComboBox, CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/SellTxForm.xaml` — ComboBox, CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/StockDividendTxForm.xaml` — ComboBox
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/TransferTxForm.xaml` — ComboBox

- [ ] **Step 1: Apply R2 substitutions to all TxForm files**

For each file above, apply the relevant rules from R2:
- All `<ComboBox` → `<ui:ComboBox` (and closing tags)
- All `<RadioButton` → `<ui:RadioButton`
- All `<ListBox` / `<ListBoxItem` → `<ui:ListBox` / `<ui:ListBoxItem`
- All `<CheckBox` → `<ui:CheckBox`
- Confirm `xmlns:ui` is present in each file's root element (add if missing per R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: Build succeeded, 0 Error(s), 0 Warning(s) related to XAML parse.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/Controls/TxForms/
git commit -m "refactor(ui): migrate TxForms controls to Wpf.Ui"
```

---

### Task 2: Portfolio/Controls — Dialogs & Panels (part 1)

**Files to modify:**
- `Assetra.WPF/Features/Portfolio/Controls/AddAssetDialog.xaml` — Button (plain), BtnIcon, ScrollViewer
- `Assetra.WPF/Features/Portfolio/Controls/AddRecordDialog.xaml` — Button (plain), RadioButton, ScrollViewer, BtnIcon
- `Assetra.WPF/Features/Portfolio/Controls/EditAssetDialog.xaml` — Button (plain), ComboBox, BtnIcon
- `Assetra.WPF/Features/Portfolio/Controls/AccountsTabPanel.xaml` — CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/AssetSections/AccountCreateSection.xaml` — CheckBox
- `Assetra.WPF/Features/Portfolio/Controls/AssetSections/CreditCardCreateSection.xaml` — CheckBox, Expander
- `Assetra.WPF/Features/Portfolio/Controls/AssetSections/LoanCreateSection.xaml` — ComboBox

- [ ] **Step 1: Apply R2, R3, R4 to dialog and panel files**

For each file:
- `<Button` (no Style) → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` on `<ui:Button` (R3)
- `<RadioButton` → `<ui:RadioButton` (R2)
- `<ComboBox` → `<ui:ComboBox` (R2)
- `<CheckBox` → `<ui:CheckBox` (R2)
- `<Expander` in `CreditCardCreateSection.xaml` — apply R4 judgment (if Header present → `<ui:CardExpander`)
- `<ScrollViewer` — keep per R6
- Confirm `xmlns:ui` on all root elements (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/Controls/
git commit -m "refactor(ui): migrate Portfolio dialog/panel controls to Wpf.Ui"
```

---

### Task 3: Portfolio/Controls — Tabs & Allocation (part 2)

**Files to modify:**
- `Assetra.WPF/Features/Portfolio/Controls/TradesTabPanel.xaml` — Button (plain), ComboBox, ListBox, CheckBox, ScrollViewer, BtnIcon, BtnIconToggle
- `Assetra.WPF/Features/Portfolio/Controls/AllocationView.xaml` — RadioButton, ListBox, ScrollViewer
- `Assetra.WPF/Features/Portfolio/Controls/PortfolioTabBar.xaml` — RadioButton
- `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml` — ListBox, CheckBox, Border
- `Assetra.WPF/Features/Portfolio/Controls/RebalanceDataGrid.xaml` — ListBox
- `Assetra.WPF/Features/Portfolio/Controls/SellPanel.xaml` — ComboBox
- `Assetra.WPF/Features/Portfolio/Controls/DashboardTabPanel.xaml` — ScrollViewer
- `Assetra.WPF/Features/Portfolio/Controls/EditTargetsOverlay.xaml` — ScrollViewer

- [ ] **Step 1: Apply R2, R3, R5 substitutions**

For each file apply relevant rules. Special notes:
- `TradesTabPanel.xaml` — `BtnIconToggle`: keep as `<ToggleButton>`, **remove** `Style="{StaticResource BtnIconToggle}"` only (R3)
- `PositionsTabPanel.xaml` — apply R5 judgment to its `<Border>` element
- `<ScrollViewer>` — keep per R6
- All `<ListBox>` / `<ListBoxItem>` → `<ui:ListBox>` / `<ui:ListBoxItem>` (R2)
- Confirm `xmlns:ui` on all root elements (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/Controls/
git commit -m "refactor(ui): migrate Portfolio tab/allocation controls to Wpf.Ui"
```

---

### Task 4: PortfolioView.xaml (root view)

**File to modify:**
- `Assetra.WPF/Features/Portfolio/PortfolioView.xaml` — Button (plain), RadioButton, ScrollViewer, Border, BtnIcon

- [ ] **Step 1: Apply R2, R3, R5**

- `<Button` (no Style) → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` + change tag (R3)
- `<RadioButton` → `<ui:RadioButton` (R2)
- `<Border>` elements — apply R5 judgment for each occurrence
- `<ScrollViewer>` — keep (R6)
- Confirm `xmlns:ui` present (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Portfolio/PortfolioView.xaml
git commit -m "refactor(ui): migrate PortfolioView controls to Wpf.Ui"
```

---

## Batch 2 — Import + FinancialOverview

### Task 5: Import + FinancialOverview

**Files to modify:**
- `Assetra.WPF/Features/Import/ImportView.xaml` — ComboBox
- `Assetra.WPF/Features/FinancialOverview/FinancialOverviewView.xaml` — Expander, ScrollViewer

- [ ] **Step 1: Apply substitutions**

`ImportView.xaml`:
- `<ComboBox` → `<ui:ComboBox` (R2)
- Confirm `xmlns:ui` (R1)

`FinancialOverviewView.xaml`:
- `<Expander` — apply R4 judgment (semantic section headers → `<ui:CardExpander`)
- `<ScrollViewer>` — keep (R6)
- Confirm `xmlns:ui` (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Import/ Assetra.WPF/Features/FinancialOverview/
git commit -m "refactor(ui): migrate Import and FinancialOverview controls to Wpf.Ui"
```

---

## Batch 3 — Reports + Categories + Recurring + Settings + Shell

### Task 6: Reports + Categories + Recurring

**Files to modify:**
- `Assetra.WPF/Features/Reports/ReportsView.xaml` — Expander, ScrollViewer
- `Assetra.WPF/Features/Categories/CategoriesView.xaml` — Button, ComboBox, RadioButton, CheckBox, Expander, ScrollViewer, BtnIcon (via Style on Button)
- `Assetra.WPF/Features/Recurring/RecurringView.xaml` — Button, ComboBox, CheckBox, ScrollViewer

- [ ] **Step 1: Apply R2, R3, R4 substitutions**

`ReportsView.xaml`:
- `<Expander` → R4 judgment
- `<ScrollViewer>` → keep (R6)

`CategoriesView.xaml`:
- `<Button` (plain) → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` (R3)
- `<ComboBox` → `<ui:ComboBox` (R2)
- `<RadioButton` → `<ui:RadioButton` (R2)
- `<CheckBox` → `<ui:CheckBox` (R2)
- `<Expander` → R4 judgment
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

`RecurringView.xaml`:
- `<Button` → `<ui:Button` (R2)
- `<ComboBox` → `<ui:ComboBox` (R2)
- `<CheckBox` → `<ui:CheckBox` (R2)
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Reports/ Assetra.WPF/Features/Categories/ Assetra.WPF/Features/Recurring/
git commit -m "refactor(ui): migrate Reports, Categories, Recurring controls to Wpf.Ui"
```

---

### Task 7: Settings + Shell

**Files to modify:**
- `Assetra.WPF/Features/Settings/SettingsView.xaml` — Button, ComboBox, RadioButton, CheckBox, ScrollViewer, BtnIcon
- `Assetra.WPF/Features/Settings/Controls/FugleHelpDialog.xaml` — Button, ScrollViewer, BtnIcon
- `Assetra.WPF/Shell/NavRailView.xaml` — RadioButton, ScrollViewer
- `Assetra.WPF/Shell/MainWindow.xaml` — ScrollViewer only (R6: no tag change needed, skip)

- [ ] **Step 1: Apply R2, R3 substitutions**

`SettingsView.xaml`:
- `<Button` (plain) → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` (R3)
- `<ComboBox` → `<ui:ComboBox` (R2)
- `<RadioButton` → `<ui:RadioButton` (R2)
- `<CheckBox` → `<ui:CheckBox` (R2)
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

`FugleHelpDialog.xaml`:
- `<Button` → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` (R3)
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

`NavRailView.xaml`:
- `<RadioButton` → `<ui:RadioButton` (R2)
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Settings/ Assetra.WPF/Shell/
git commit -m "refactor(ui): migrate Settings and Shell controls to Wpf.Ui"
```

---

## Batch 4 — Goals + Fire + MonteCarlo + Retirement + misc

### Task 8: Goals + Fire + MonteCarlo + Retirement

**Files to modify:**
- `Assetra.WPF/Features/Goals/GoalsView.xaml` — Button, ScrollViewer, BtnIcon
- `Assetra.WPF/Features/Fire/FireView.xaml` — Border
- `Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml` — Border
- `Assetra.WPF/Features/Retirement/RetirementView.xaml` — Button, ComboBox

- [ ] **Step 1: Apply R2, R3, R5 substitutions**

`GoalsView.xaml`:
- `<Button` → `<ui:Button` (R2)
- `Style="{StaticResource BtnIcon}"` → `Appearance="Transparent"` (R3)
- `<ScrollViewer>` → keep (R6)
- Confirm `xmlns:ui` (R1)

`FireView.xaml`:
- `<Border` elements → apply R5 judgment
- Confirm `xmlns:ui` if any Border converted to ui:Card (R1)

`MonteCarloView.xaml`:
- `<Border` elements → apply R5 judgment
- Confirm `xmlns:ui` if any Border converted to ui:Card (R1)

`RetirementView.xaml`:
- `<Button` → `<ui:Button` (R2)
- `<ComboBox` → `<ui:ComboBox` (R2)
- Confirm `xmlns:ui` (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/Goals/ Assetra.WPF/Features/Fire/ Assetra.WPF/Features/MonteCarlo/ Assetra.WPF/Features/Retirement/
git commit -m "refactor(ui): migrate Goals, Fire, MonteCarlo, Retirement controls to Wpf.Ui"
```

---

### Task 9: Shared Controls + Snackbar + Trends

**Files to modify:**
- `Assetra.WPF/Controls/DateRangePicker.xaml` — Button
- `Assetra.WPF/Controls/HexColorPicker.xaml` — Button
- `Assetra.WPF/Features/Snackbar/SnackbarView.xaml` — Button
- `Assetra.WPF/Features/Trends/TrendsView.xaml` — Button, BtnGhost (or BtnIcon)

- [ ] **Step 1: Apply R2, R3 substitutions**

`DateRangePicker.xaml` and `HexColorPicker.xaml`:
- `<Button` → `<ui:Button` (R2)
- Confirm `xmlns:ui` (R1)

`SnackbarView.xaml`:
- `<Button` → `<ui:Button` (R2)
- Confirm `xmlns:ui` (R1)

`TrendsView.xaml`:
- `<Button` (plain) → `<ui:Button` (R2)
- `Style="{StaticResource BtnGhost}"` → `Appearance="Secondary"` (R3)
- Confirm `xmlns:ui` (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Controls/ Assetra.WPF/Features/Snackbar/ Assetra.WPF/Features/Trends/
git commit -m "refactor(ui): migrate shared Controls, Snackbar, Trends to Wpf.Ui"
```

---

## Batch 5 — Remaining modules + GlobalStyles cleanup

### Task 10: RealEstate + PhysicalAsset + Insurance + Alerts + Reconciliation

**Files to modify:**
- `Assetra.WPF/Features/RealEstate/RealEstateView.xaml` — Button, CheckBox
- `Assetra.WPF/Features/PhysicalAsset/PhysicalAssetView.xaml` — Button, ComboBox
- `Assetra.WPF/Features/Insurance/InsurancePolicyView.xaml` — Button, ComboBox
- `Assetra.WPF/Features/Alerts/AlertsView.xaml` — Button, ComboBox, ListBox, Border
- `Assetra.WPF/Features/Reconciliation/ReconciliationView.xaml` — ComboBox, RadioButton

- [ ] **Step 1: Apply R2, R5 substitutions**

For each file apply relevant rules from R2:
- `<Button` → `<ui:Button`
- `<ComboBox` → `<ui:ComboBox`
- `<RadioButton` → `<ui:RadioButton`
- `<ListBox` / `<ListBoxItem` → `<ui:ListBox` / `<ui:ListBoxItem`
- `<CheckBox` → `<ui:CheckBox`
- `<Border` in `AlertsView.xaml` → apply R5 judgment
- Confirm `xmlns:ui` on all root elements (R1)

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Features/RealEstate/ Assetra.WPF/Features/PhysicalAsset/ Assetra.WPF/Features/Insurance/ Assetra.WPF/Features/Alerts/ Assetra.WPF/Features/Reconciliation/
git commit -m "refactor(ui): migrate RealEstate, PhysicalAsset, Insurance, Alerts, Reconciliation to Wpf.Ui"
```

---

### Task 11: GlobalStyles.xaml — DataGrid unified style

**File to modify:**
- `Assetra.WPF/Themes/GlobalStyles.xaml`

- [ ] **Step 1: Add DataGrid global style**

In `GlobalStyles.xaml`, locate the section after the last existing implicit style (before the closing `</ResourceDictionary>`) and add:

```xml
<!--  ── DataGrid unified style (Wpf.Ui token integration) ──  -->
<Style TargetType="{x:Type DataGrid}"
       BasedOn="{StaticResource {x:Type DataGrid}}">
    <Setter Property="RowHeight" Value="40" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="GridLinesVisibility" Value="Horizontal" />
    <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource ControlStrokeColorDefaultBrush}" />
    <Setter Property="RowBackground" Value="Transparent" />
    <Setter Property="AlternatingRowBackground" Value="Transparent" />
    <Setter Property="ColumnHeaderHeight" Value="36" />
</Style>
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assetra.WPF/Themes/GlobalStyles.xaml
git commit -m "refactor(ui): add unified DataGrid style using Wpf.Ui tokens"
```

---

### Task 12: GlobalStyles.xaml — Remove custom Button style definitions

**Prerequisite:** All per-file BtnPrimary / BtnGhost / BtnIcon / BtnIconToggle Style references must already be removed (Tasks 1-10 complete).

**File to modify:**
- `Assetra.WPF/Themes/GlobalStyles.xaml`

- [ ] **Step 1: Verify zero remaining references**

```bash
grep -r "BtnGhost\|BtnPrimary\|BtnIcon\|BtnIconToggle" Assetra.WPF --include="*.xaml" --exclude-dir=Themes
```
Expected: no output (zero matches).

If any matches remain, fix those files first before proceeding.

- [ ] **Step 2: Remove Style blocks from GlobalStyles.xaml**

Delete the following four `<Style>` blocks entirely from `GlobalStyles.xaml`:
- `<Style x:Key="BtnPrimary" TargetType="Button">` … `</Style>` (lines ~255-294)
- `<Style x:Key="BtnGhost" TargetType="Button">` … `</Style>` (lines ~297-335)
- `<Style x:Key="BtnIcon" TargetType="Button">` … `</Style>` (lines ~376-422)
- `<Style x:Key="BtnIconToggle" TargetType="ToggleButton">` … `</Style>` (lines ~425-466)

**Keep intact:** `BtnPeriod`, `DataGridDeleteBtn`, and all other styles.

- [ ] **Step 3: Build and verify**

```bash
dotnet build Assetra.slnx
```
Expected: 0 errors. A `StaticResource not found` error means a reference was missed in Step 1.

- [ ] **Step 4: Run tests**

```bash
dotnet test Assetra.Tests/Assetra.Tests.csproj
```
Expected: All tests pass.

- [ ] **Step 5: Final grep — confirm no standard controls remain**

```bash
grep -rn "<Button\b\|<ComboBox\b\|<RadioButton\b\|<ListBox\b\|<CheckBox\b" Assetra.WPF --include="*.xaml" | grep -v "Themes/" | grep -v "Languages/"
```
Expected: no output, or only occurrences inside data templates where `x:DataType` context prevents `ui:` usage (document any such exceptions in a comment).

- [ ] **Step 6: Commit**

```bash
git add Assetra.WPF/Themes/GlobalStyles.xaml
git commit -m "refactor(ui): remove BtnPrimary/BtnGhost/BtnIcon/BtnIconToggle custom styles"
```

---

## Final verification

- [ ] `dotnet build Assetra.slnx` — 0 errors, 0 XAML parse warnings
- [ ] `dotnet test Assetra.Tests/Assetra.Tests.csproj` — all pass
- [ ] `dotnet format --verify-no-changes` — no formatting drift
