# Assetra Architecture

## Layers

- `Assetra.Core`
  - Domain models and repository/service contracts.
- `Assetra.Application`
  - Organized by context folder. Currently:
    - `Portfolio/` — load / summary / query / transaction workflow services for positions, cash, liabilities, credit cards, trades.
    - `Alerts/` — alert rule evaluation behind `IAlertService`.
    - `Loans/` — loan amortization and payment workflows.
    - `Budget/` — budget plans, categories, monthly summary (`MonthlyBudgetSummaryService`).
    - `Recurring/` — recurring/subscription transactions (`RecurringTransactionScheduler`, pending entry handling).
    - `Reports/` — period reports (`MonthEndReportService`).
  - Each context exposes query services, workflow services, and summary services as needed.
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
- `RecurringViewModel` and `CategoriesViewModel` (expense categories / budgets) wire into the `Recurring` and `Budget` application contexts respectively.
- `DashboardViewModel` composes net-worth and budget summaries from `Portfolio` and `Budget` query services.

## Guardrails

- New UI features should first decide whether they are:
  - UI state
  - Query composition
  - Mutation workflow
  - Pure calculation
- Prefer adding tests at the workflow/query layer before relying only on ViewModel tests.
