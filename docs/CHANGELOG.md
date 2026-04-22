# Changelog

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
