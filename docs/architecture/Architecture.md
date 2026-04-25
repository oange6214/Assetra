# Assetra Architecture

## Layers

- `Assetra.Core`
  - Domain models and repository/service contracts.
- `Assetra.Application`
  - Alert services, query services, workflow services, and summary/load orchestration.
- `Assetra.Infrastructure`
  - SQLite repositories, external market-data clients, schedulers, migration helpers.
- `Assetra.WPF`
  - Views, ViewModels, controllers, sub-viewmodels, startup wiring, and UI-only services.

## Dependency Rules

- `WPF -> Application -> Core`
- `Infrastructure` implements `Core` and `Application` contracts.
- ViewModels should not directly mutate repositories.
- Repository classes should not own schema migration or startup repair logic.

## Service Conventions

- `*WorkflowService`
  - Mutating application flow, often coordinates multiple repositories/services.
- `*QueryService`
  - Read-only composition for UI or reporting.
- `*SummaryService`
  - Pure calculations without persistence.
- `*Migrator` / `*Initializer` / `*RepairService`
  - Database evolution and startup repair responsibilities.

## Portfolio Module

- Main WPF shell depends on `PortfolioViewModel`.
- `PortfolioViewModel` now delegates most mutation flow to app-layer workflow services.
- UI-specific orchestration is being moved into WPF-side controllers and sub-viewmodels to keep the ViewModel thinner.

## Other Modules

- `AlertsViewModel` now depends on an application-layer alert service rather than a repository.
- `FinancialOverviewViewModel` reads through an application query service instead of composing repository calls directly in the ViewModel.

## Guardrails

- New UI features should first decide whether they are:
  - UI state
  - Query composition
  - Mutation workflow
  - Pure calculation
- Prefer adding tests at the workflow/query layer before relying only on ViewModel tests.
