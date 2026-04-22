# Changelog

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
