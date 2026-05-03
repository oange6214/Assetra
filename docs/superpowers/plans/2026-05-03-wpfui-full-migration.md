# Wpf.Ui Full Migration Implementation Plan (Revised)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all remaining standard WPF controls (Button, DataGrid, Expander, Border) with Wpf.Ui 4.2.0 equivalents.

**Architecture:** Four focused tasks grouped by control type. ComboBox / RadioButton / CheckBox / ListBox are intentionally left as standard WPF — Wpf.Ui does not provide ui: wrappers for these; they are styled automatically by `ControlsDictionary`. Custom button styles (BtnPrimary, BtnGhost, BtnIcon, BtnIconToggle) have already been removed in a prior sprint.

**Tech Stack:** .NET 10 / WPF / Wpf.Ui 4.2.0 / XAML

---

## Transformation Rules

### R1 — xmlns:ui namespace
Every modified XAML file must declare:
```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```
Add to root element if missing.

### R2 — Button → ui:Button
Replace `<Button` / `</Button>` with `<ui:Button` / `</ui:Button>`.

**Exception:** Keep as standard `<Button>` when it uses a custom DataTemplate style (e.g., `Style="{StaticResource DayCellButton}"`).

```xml
<!-- Before -->
<Button Background="Transparent" BorderThickness="0" Command="{Binding DeleteCommand}">
    <ui:SymbolIcon Symbol="Delete24" />
</Button>

<!-- After -->
<ui:Button Appearance="Transparent" Command="{Binding DeleteCommand}">
    <ui:SymbolIcon Symbol="Delete24" />
</ui:Button>
```

When changing a transparent/borderless button to `ui:Button Appearance="Transparent"`, also **remove** the now-redundant `Background="Transparent"` and `BorderThickness="0"` attributes.

### R3 — DataGrid → ui:DataGrid
Replace `<DataGrid` / `</DataGrid>` with `<ui:DataGrid` / `</ui:DataGrid>`.
Self-closing: `<DataGrid ... />` → `<ui:DataGrid ... />`.

```xml
<!-- Before -->
<DataGrid AutoGenerateColumns="False" ItemsSource="{Binding Items}">

<!-- After -->
<ui:DataGrid AutoGenerateColumns="False" ItemsSource="{Binding Items}">
```

### R4 — Expander → ui:CardExpander
Replace when Expander has a meaningful `Header` string that labels a content section.
Keep as standard `<Expander>` when no semantic header is present.

```xml
<!-- Semantic header → replace -->
<Expander Header="{DynamicResource Reports.IncomeStatement.Title}" IsExpanded="False">
    ...
</Expander>
<!-- becomes -->
<ui:CardExpander Header="{DynamicResource Reports.IncomeStatement.Title}" IsExpanded="False">
    ...
</ui:CardExpander>
```

### R5 — Border → ui:Card (semantic containers only)
Replace **only** when ALL three conditions hold:
1. Has `Padding` attribute
2. Contains multiple child elements
3. Not a clip mask / animation target / single-child wrapper

```xml
<!-- Before: semantic container with padding + multiple children -->
<Border Padding="16" Background="{DynamicResource AppSurface}" CornerRadius="8">
    <StackPanel>...</StackPanel>
</Border>
<!-- After -->
<ui:Card Padding="16">
    <StackPanel>...</StackPanel>
</ui:Card>
```

When converting to `ui:Card`, **remove** `Background`, `BorderBrush`, `BorderThickness`, and `CornerRadius` attributes — `ui:Card` provides these from the theme.

---

## Task A: DataGrid → ui:DataGrid

**Files to modify (11 files):**

| File | Notes |
|------|-------|
| `Assetra.WPF/Features/Alerts/AlertsView.xaml:380` | |
| `Assetra.WPF/Features/Fire/FireView.xaml:103` | |
| `Assetra.WPF/Features/Insurance/InsurancePolicyView.xaml:64` | |
| `Assetra.WPF/Features/PhysicalAsset/PhysicalAssetView.xaml:65` | |
| `Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml:111` | |
| `Assetra.WPF/Features/RealEstate/RealEstateView.xaml:56` | |
| `Assetra.WPF/Features/Retirement/RetirementView.xaml:61` | |
| `Assetra.WPF/Features/Portfolio/Controls/AllocationView.xaml:218` | |
| `Assetra.WPF/Features/Portfolio/Controls/AccountsTabPanel.xaml:215` | |
| `Assetra.WPF/Features/Portfolio/Controls/LiabilityTabPanel.xaml:347` | |
| `Assetra.WPF/Features/Portfolio/Controls/TradesTabPanel.xaml:584` | |

- [ ] **Step 1: Replace DataGrid tags in all 11 files**

For each file: replace `<DataGrid` with `<ui:DataGrid` and `</DataGrid>` with `</ui:DataGrid>`.
Confirm `xmlns:ui` is declared on each file's root element (R1).

- [ ] **Step 2: Build**

```bash
dotnet build D:\Workspaces\Finances\Assetra\Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
cd D:\Workspaces\Finances\Assetra
git add Assetra.WPF/Features/Alerts/AlertsView.xaml
git add Assetra.WPF/Features/Fire/FireView.xaml
git add Assetra.WPF/Features/Insurance/InsurancePolicyView.xaml
git add Assetra.WPF/Features/PhysicalAsset/PhysicalAssetView.xaml
git add Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml
git add Assetra.WPF/Features/RealEstate/RealEstateView.xaml
git add Assetra.WPF/Features/Retirement/RetirementView.xaml
git add Assetra.WPF/Features/Portfolio/Controls/AllocationView.xaml
git add Assetra.WPF/Features/Portfolio/Controls/AccountsTabPanel.xaml
git add Assetra.WPF/Features/Portfolio/Controls/LiabilityTabPanel.xaml
git add Assetra.WPF/Features/Portfolio/Controls/TradesTabPanel.xaml
git commit -m "refactor(ui): migrate DataGrid to ui:DataGrid"
```

---

## Task B: Expander → ui:CardExpander

**Files to modify (4 files, 8 Expanders):**

| File | Lines | Has Header? | Action |
|------|-------|------------|--------|
| `Assetra.WPF/Features/Reports/ReportsView.xaml` | 369, 396, 427, 466, 505 | Yes (DynamicResource titles) | → `ui:CardExpander` |
| `Assetra.WPF/Features/Categories/CategoriesView.xaml` | 694, 1050 | Check at runtime | → apply R4 judgment |
| `Assetra.WPF/Features/Portfolio/Controls/AssetSections/CreditCardCreateSection.xaml` | 58 | Check (no explicit Header in scan) | → apply R4 judgment |

- [ ] **Step 1: Replace Expander tags**

For `ReportsView.xaml` lines 369, 396, 427, 466, 505:
- All have `Header="{DynamicResource ...}"` → replace with `<ui:CardExpander` / `</ui:CardExpander>`

For `CategoriesView.xaml` lines 694 and 1050:
- Read the actual content. If `Header=` attribute is present → `ui:CardExpander`. If not → keep.

For `CreditCardCreateSection.xaml` line 58:
- Read the actual content. Apply R4: Header present → `ui:CardExpander`, no Header → keep.

Confirm `xmlns:ui` on all modified files (R1).

- [ ] **Step 2: Build**

```bash
dotnet build D:\Workspaces\Finances\Assetra\Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
cd D:\Workspaces\Finances\Assetra
git add Assetra.WPF/Features/Reports/ReportsView.xaml
git add Assetra.WPF/Features/Categories/CategoriesView.xaml
git add Assetra.WPF/Features/Portfolio/Controls/AssetSections/CreditCardCreateSection.xaml
git commit -m "refactor(ui): migrate Expander to ui:CardExpander"
```

---

## Task C: Border → ui:Card

**Candidates (apply R5 judgment to each):**

| File | Line | Padding | Multi-child | Replace? |
|------|------|---------|------------|---------|
| `Assetra.WPF/Features/Fire/FireView.xaml` | 32 | ✓ 16 | ✓ StackPanel w/ children | Yes |
| `Assetra.WPF/Features/Fire/FireView.xaml` | 73 | ✓ 16 | ✓ | Yes |
| `Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml` | 32 | ✓ 16 | ✓ | Yes |
| `Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml` | 75 | ✓ 16 | ✓ | Yes |
| `Assetra.WPF/Features/Alerts/AlertsView.xaml` | 794 | ✓ 32 | ✓ empty state StackPanel | Yes |
| `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml` | 844 | ✓ 32 | ✓ empty state StackPanel | Yes |
| `Assetra.WPF/Features/Portfolio/PortfolioView.xaml` | 1058 | ✓ 8,2 | ✗ single TextBlock child | No — keep Border |

- [ ] **Step 1: Replace Border with ui:Card in the 6 qualifying files**

For each qualifying Border:
- Replace `<Border` with `<ui:Card`
- Replace `</Border>` with `</ui:Card>`
- Remove the following attributes (ui:Card provides them from theme): `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`
- Keep: `Padding`, `Margin`, `Grid.Row`, `Visibility`, and any other layout/binding attributes

Example for `FireView.xaml:32`:
```xml
<!-- Before -->
<Border Grid.Row="1" Padding="16"
        Background="{DynamicResource AppSurface}"
        BorderBrush="{DynamicResource AppBorder}"
        BorderThickness="1"
        CornerRadius="8">
    <StackPanel>...</StackPanel>
</Border>

<!-- After -->
<ui:Card Grid.Row="1" Padding="16">
    <StackPanel>...</StackPanel>
</ui:Card>
```

Confirm `xmlns:ui` on all modified files (R1).

- [ ] **Step 2: Build**

```bash
dotnet build D:\Workspaces\Finances\Assetra\Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
cd D:\Workspaces\Finances\Assetra
git add Assetra.WPF/Features/Fire/FireView.xaml
git add Assetra.WPF/Features/MonteCarlo/MonteCarloView.xaml
git add Assetra.WPF/Features/Alerts/AlertsView.xaml
git add Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml
git commit -m "refactor(ui): migrate semantic Border to ui:Card"
```

---

## Task D: Button → ui:Button

**Files to modify (9 files, 19 Button instances):**

| File | Lines | Notes |
|------|-------|-------|
| `Assetra.WPF/Controls/HexColorPicker.xaml` | 83 | Color swatch button |
| `Assetra.WPF/Controls/DateRangePicker.xaml` | 137, 231, 301 | Clear + prev/next month nav. **Skip** lines 276 and 336 — `Style="{StaticResource DayCellButton}"` (custom DataTemplate style, keep as standard Button) |
| `Assetra.WPF/Features/PhysicalAsset/PhysicalAssetView.xaml` | 108, 114 | DataGrid action column |
| `Assetra.WPF/Features/Insurance/InsurancePolicyView.xaml` | 107, 113 | DataGrid action column |
| `Assetra.WPF/Features/RealEstate/RealEstateView.xaml` | 95, 101 | DataGrid action column |
| `Assetra.WPF/Features/Recurring/RecurringView.xaml` | 324, 386, 391 | Grid action buttons |
| `Assetra.WPF/Features/Retirement/RetirementView.xaml` | 108, 114, 120 | DataGrid action column |
| `Assetra.WPF/Features/Snackbar/SnackbarView.xaml` | 56 | Dismiss button |

- [ ] **Step 1: Replace Button tags**

For each file listed above:
1. Read the file and locate the Button instances
2. Replace `<Button` with `<ui:Button` and `</Button>` with `</ui:Button>`
3. If the button has `Background="Transparent"` and `BorderThickness="0"` (transparent icon-style button):
   - Add `Appearance="Transparent"` to the `<ui:Button`
   - Remove `Background="Transparent"` and `BorderThickness="0"` attributes
4. Skip any `<Button>` with `Style="{StaticResource DayCellButton}"` — keep as standard Button
5. Confirm `xmlns:ui` on root element (R1)

Example for transparent icon buttons (DateRangePicker nav, Snackbar dismiss):
```xml
<!-- Before -->
<Button Width="24" Height="24" Padding="0"
        Background="Transparent"
        BorderThickness="0"
        Click="PrevMonth_Click" Cursor="Hand">
    <ui:SymbolIcon Symbol="ChevronLeft24" />
</Button>

<!-- After -->
<ui:Button Width="24" Height="24" Padding="0"
           Appearance="Transparent"
           Click="PrevMonth_Click" Cursor="Hand">
    <ui:SymbolIcon Symbol="ChevronLeft24" />
</ui:Button>
```

- [ ] **Step 2: Build**

```bash
dotnet build D:\Workspaces\Finances\Assetra\Assetra.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Run tests**

```bash
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj
```
Expected: All tests pass.

- [ ] **Step 4: Final verification — confirm no standard controls remain**

```bash
grep -rn "<Button\b\|<DataGrid\b\|<Expander\b" D:/Workspaces/Finances/Assetra/Assetra.WPF --include="*.xaml" | grep -v "Themes/" | grep -v "Languages/" | grep -v "DayCellButton\|DataGridDeleteBtn"
```
Expected: empty output (or only the DayCellButton exceptions).

- [ ] **Step 5: Commit**

```bash
cd D:\Workspaces\Finances\Assetra
git add Assetra.WPF/Controls/HexColorPicker.xaml
git add Assetra.WPF/Controls/DateRangePicker.xaml
git add Assetra.WPF/Features/PhysicalAsset/PhysicalAssetView.xaml
git add Assetra.WPF/Features/Insurance/InsurancePolicyView.xaml
git add Assetra.WPF/Features/RealEstate/RealEstateView.xaml
git add Assetra.WPF/Features/Recurring/RecurringView.xaml
git add Assetra.WPF/Features/Retirement/RetirementView.xaml
git add Assetra.WPF/Features/Snackbar/SnackbarView.xaml
git commit -m "refactor(ui): migrate remaining Button to ui:Button"
```
