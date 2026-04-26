# Changelog

## v0.5.8 - 2026-04-26

This release polishes localized hint copy and tidies project documentation.

### Highlights

- Refined Cash / Credit Card / Liability add-dialog hint strings in zh-TW and en-US so they reflect the current "record initial entry on create" flow instead of the older "go add a record afterwards" wording.
- Backfilled missing v0.4.1, v0.5.6, v0.5.7 entries in `CHANGELOG.md`.
- Removed dead `Downloads` links from `docs/INDEX.md`.

## v0.5.7 - 2026-04-26

This release polishes responsive UX across the portfolio dialogs and tables.

### Highlights

- Forced horizontal two-column layout for toggle pairs in 新增紀錄 dialog (no more vertical fallback at narrow widths).
- Fixed hover rectangle overflow on investment Position cards by overriding the default `ListBoxItem` template.
- Replaced fixed-width `WrapPanel` with `UniformGrid` so position stat cells fill available width without trailing gaps.
- Renamed Accounts / Liability DataGrid first column header from "資產" to "名稱"; renamed investment column header to "標的".
- Refactored Accounts / Liability cell template to a single-row `Grid` so the default badge vertically centers against the full cell height.
- Updated 新增紀錄 footer to follow Windows dialog convention (取消 left, 確認 right) and shortened wording from "取消編輯" to "取消".

## v0.5.6 - 2026-04-25

This release fixes a regression in the liability creation flow.

### Highlights

- Fixed liability creation dialog not showing the loan section under certain states.

## v0.5.5 - 2026-04-24

This release refines startup reliability and recovery behavior.

### Highlights

- Startup no longer blocks every normal launch on update checks.
- Added recovery-only startup update checks when the previous launch did not complete.
- Keeps faster day-to-day startup while preserving a repair path for broken installs.

### Notable internal changes

- Added `startup.pending` marker flow in `App.xaml.cs`.
- Normal startup now checks for updates in the background after the main window appears.
- Recovery update path runs only when the previous startup likely failed.

## v0.5.4 - 2026-04-24

This release improves Windows application branding fidelity.

### Highlights

- Replaced the blurry Windows app icon path with a dedicated multi-size Windows `.ico`.
- App / window icon now uses a Windows-specific asset instead of the web favicon.

### Notable internal changes

- Added `Assets/windows/assetra-app.ico`.
- Updated WPF project icon wiring and main window icon resource.

## v0.5.3 - 2026-04-24

This release introduced a startup-first update safety net after a broken-install incident.

### Highlights

- Added update checks before the main window during startup.
- Added self-repair update attempts on startup failure.

### Notable internal changes

- This was an intermediate safety release and was later refined in `v0.5.5`.

## v0.5.2 - 2026-04-24

This release fixes a startup crash and hardens currency loading.

### Highlights

- Fixed a crash caused by badge styling during initial UI load.
- Hardened Frankfurter exchange-rate parsing against missing JSON fields.

### Notable internal changes

- `PortfolioBadgeBase` no longer resolves `CornerRadius` through a fragile startup path.
- `CurrencyService` now uses tolerant JSON parsing and safe fallbacks.

## v0.5.1 - 2026-04-24

This release fixes splash/icon startup reliability.

### Highlights

- Fixed startup failure caused by splash icon resource loading.
- Improved startup resiliency for packaged installs.

### Notable internal changes

- Splash icon loading moved out of static XAML resource resolution and into code.
- Resource packaging rules were tightened for icon assets.

## v0.5.0 - 2026-04-24

This release adds credit-card flows and a broad responsive UI pass.

### Highlights

- Added credit card asset and transaction flows.
- Reorganized branding assets and package logo pipeline.
- Polished responsive portfolio, alerts, settings, shell, and dialog layouts.

### Notable internal changes

- Added credit card workflows, schema support, and regression tests.
- Reworked many WPF layouts to better support larger `UiScale` and narrower widths.

## v0.4.1 - 2026-04-23

This release adds in-app guidance for Fugle API key setup.

### Highlights

- Added a Fugle help dialog accessible from the Settings page.
- Surfaced setup steps in zh-TW and en-US so users can configure the key without leaving the app.

## v0.4.0 - 2026-04-23

This release focuses on safer portfolio editing and configurable market-data sourcing.

### Highlights

- Added `Fugle` as a configurable live quote and historical price source.
- Added `Settings` fields for quote source, history source, and local Fugle API key storage.
- Added documentation for safe Fugle API key setup outside Git.
- Reworked record editing into:
  - safe edit mode
  - create revision
  - replace original / keep both flow
- Replaced the generic welcome banner with task-oriented setup notices.

### Notable internal changes

- `StockScheduler` can now use Fugle and fall back to official TWSE/TPEX sources.
- `DynamicHistoryProvider` supports `fugle` alongside `twse`, `yahoo`, and `finmind`.
- `PortfolioViewModel` and tests now preserve backward-compatible construction paths while using the newer app-layer services.

## v0.3.0 - 2026-04-22

This release marks the first architecture-focused milestone for Assetra.

### Highlights

- Added a clearer `Core -> Application -> Infrastructure -> WPF` structure.
- Moved most `Portfolio` mutation flows into application workflow services.
- Moved summary/load/history/query responsibilities into dedicated application services.
- Modularized startup, schema migration, and repository initialization responsibilities.
- Introduced thinner WPF-side controllers/sub-viewmodels for parts of the `Portfolio` UI.
- Brought `Alerts` back behind an application-layer contract (`IAlertService`).
- Added workflow-level tests and architecture documentation.

### Notable internal changes

- `PortfolioViewModel` now delegates more behavior to:
  - workflow services
  - query services
  - WPF-side controllers/sub-viewmodels
- `FinancialOverviewViewModel` now reads through an application query service.
- `.superpowers/` local tool artifacts are ignored and no longer tracked.

## v0.2.0

Earlier product milestone prior to the application-layer and architecture cleanup work.
