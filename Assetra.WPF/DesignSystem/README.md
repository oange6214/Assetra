# Assetra WPF DesignSystem

This folder owns Assetra's WPF-native visual language. Feature pages should consume these resources instead of defining local button, tab, input, empty-state, dialog, or list styles.

## Design Direction

Assetra Native UI = **Fluent visual language + Carbon data patterns**.

- **Fluent-first** for visual language and control behavior: shell, navigation rail, buttons, inputs, ComboBox, DatePicker, CheckBox, RadioButton, Tabs, DataGrid, Expander, Dialog, focus visuals, theme behavior, motion, accessibility, and native Windows ergonomics (keyboard tab order, Escape/cancel, selected state clarity).
- **Carbon-assisted** for information architecture in dense finance screens: data table density, filter toolbars, empty state hierarchy, list/detail patterns, report section structure, dashboard groupings, and form vertical rhythm.
- **Assetra-owned** for tokens, components, resource names, implementation, finance-specific rules, localization, and release gates. Carbon and Fluent are references, **not runtime dependencies**.

The combined rule: *Fluent decides how the product feels. Carbon helps decide how dense product information is organized.*

For full ownership matrix, page patterns, phase plan, and acceptance rules see [`docs/planning/Assetra-Fluent-Carbon-UI-Plan.md`](../../docs/planning/Assetra-Fluent-Carbon-UI-Plan.md).

### Non-Goals

- Do not add Carbon or any other web design system as a runtime dependency.
- Do not add WPF-UI back as a visual dependency.
- Do not copy Carbon's exact visual style, black/gray palette, or web component spacing.
- Do not build a Tailwind-like utility class system in XAML.
- Do not let feature pages define their own buttons, tabs, empty states, dialogs, or input styles.

## Resource Ownership

- `Tokens/` contains theme-independent primitive values: colour scales, spacing, radius, typography, sizing, border, shadow, and motion.
- `Themes/Light.xaml` and `Themes/Dark.xaml` contain theme-specific semantic brushes. Runtime theme switching should replace only these dictionaries.
- `Styles/` contains reusable layout patterns and style families that are not tied to a custom control type.
- `Controls/` contains WPF control templates and control-specific variants. Some files are compatibility shims that import the canonical style dictionary.
- `Resources/` is reserved for icons, drawings, and shared non-style assets.

## Canonical Style Files

- `Styles/Inputs.xaml` owns `AppTextBox`, `AppNumberTextBox`, `AppMoneyTextBox`, `AppRateTextBox`, `AppPercentTextBox`, `FormComboBox`, `AppComboBox`, and input display templates.
- `Styles/Tabs.xaml` owns `NavTabBtn`, `SegmentBtn`, `AppTopTab`, `AppSubTab`, `AppSegmentButton`, and implicit `TabControl` / `TabItem` styles.
- `Styles/DataGrid.xaml` owns `AppDataGrid`, row styles, cell styles, and column header styles.
- `Styles/EmptyState.xaml` owns empty-state layout, text, and icon styles.
- `Controls/Button.xaml` owns button variants, including empty-state action button variants.
- `Controls/Button.xaml` also owns `AppButtonContent`, `AppButtonText`,
  `AppButtonLeadingIcon`, and `AppButtonTrailingIcon` for icon + text button
  content. Use these instead of hand-binding foreground on every button label.
- `Controls/TextBox.xaml`, `Controls/ComboBox.xaml`, `Controls/DataGrid.xaml`, `Controls/Tab.xaml`, and `Controls/EmptyState.xaml` are compatibility shims. Do not add new implementations there.

## Naming Rules

- Primitive tokens use `Color.*`, `Space.*`, `Radius.*`, `Font.*`, `Control.*`, `Border.*`, `Shadow.*`, and `Motion.*`.
- Semantic brushes use `Brush.*`.
- Backward-compatible app aliases such as `AppBackground`, `AppSurface`, and `AppAccent` remain while pages migrate.
- Component styles use `App*` or page-scoped patterns already centralized in `Styles/`.
- Layout styles use `Page*`, `Form*`, `List*`, `Dashboard*`, or `Dialog*`.

## Migration Rules

- Do not add page-local button, tab, input, or empty-state styles.
- Do not introduce new direct `ui:` usage in migrated pages.
- If a page needs a visual pattern that does not exist here yet, add it to DesignSystem first and then consume it from the page.
- Do not add `BasedOn="{StaticResource ...}"` to a key from another ResourceDictionary. Put related variants in the same file, or make the file a compatibility shim that merges the canonical dictionary.
- Run `tools/Scan-XamlResources.ps1 -FailOnExternalBasedOn` after changing DesignSystem resource dictionaries.

## Usage Guide

See `USAGE.md` for the canonical page, form, dialog, empty-state, input, button, list, and table patterns.

## Visual Regression Gallery

The control gallery lives at `DesignSystem/ControlGallery.xaml`. Regenerate the
light and dark screenshots with:

```powershell
.\tools\Capture-ControlGallery.ps1
```

The generated screenshots are stored in `docs/reviews/control-gallery-light.png`
and `docs/reviews/control-gallery-dark.png`.
