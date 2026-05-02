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
    - `Reports/` — three-statement reports (`IncomeStatementService`, `BalanceSheetService`, `CashFlowStatementService`, `ReportExportService`). *(v0.11)*
    - `Analysis/` — performance (XIRR / TWR / MWR / benchmark / PnL attribution) and risk (volatility / drawdown / Sharpe / concentration). *(v0.12–v0.13)*
    - `Goals/` — complete goal subsystem (`GoalPlanningService`, `GoalProgressQueryService`, `GoalFundingRule`, `GoalMilestone`). *(v0.16)*
    - `Tax/` — tax summary, dividend / overseas income tracking, export. *(v0.18)*
    - `Import/` + `Reconciliation/` — full import pipeline (CSV / Excel / PDF / OCR), AutoCategorizationRule, batch history + rollback, reconciliation sessions. *(v0.7–v0.19)*
    - `Sync/` — `SyncOrchestrator`, `CompositeLocalChangeQueue`, `LastWriteWinsResolver`, `InMemorySyncMetadataStore`. *(v0.20–v0.21)*
    - `Fx/` — `IFxRateProvider`, `StaticFxRateProvider`, `MultiCurrencyValuationService`. *(v0.14)*
    - `MultiAsset/` — `RealEstateValuationService`, `InsuranceCashValueCalculator`, `RetirementProjectionService`, `PhysicalAssetValuationService`. *(✅ v0.22.0)*
    - `Simulation/` — `FireCalculator`, `SustainabilityAnalyzer`, `MonteCarloSimulator`, `StochasticRateProvider`（純計算，無持久化）. *(✅ v0.22.0)*
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

## WPF Shell Navigation

- `NavSection` is the source of truth for top-level shell destinations.
- `NavRailView` exposes one navigation button per visible `NavSection`.
- `MainViewModel` owns the top-level feature ViewModels consumed by shell content templates.
- `MainWindow` renders the active destination through one `ContentControl` and `DataTemplate` switching keyed by `NavRail.ActiveSection`.
- When adding a top-level feature page, update all four shell surfaces together:
  - `NavSection`
  - `NavRailView.xaml`
  - `MainViewModel`
  - `MainWindow.xaml` content template
- Use `Features/<Context>/<Context>View.xaml` for top-level pages.
- Use `Controls/` for page-local reusable UI and `SubViewModels/` for page-local state objects.
- Dialog and overlay controls should stay page-local unless another context genuinely reuses them.

## Multi-Asset Entities (✅ v0.22.0)

All new multi-asset entities (`RealEstate`, `InsurancePolicy`, `RetirementAccount`, `PhysicalAsset`) must:
- Include `EntityVersion` for sync readiness
- Register in `CompositeLocalChangeQueue` entity routing
- Feed `BalanceSheetService` aggregation (asset-side line items)

## Simulation Context (✅ v0.22.0)

`Simulation/` context is **calculation-only**:
- No persistence — results are transient
- Input from existing Portfolio / Budgeting / MultiAsset query services
- Output directly to ViewModel for chart rendering (LiveChartsCore)

## Guardrails

- New UI features should first decide whether they are:
  - UI state
  - Query composition
  - Mutation workflow
  - Pure calculation
- Prefer adding tests at the workflow/query layer before relying only on ViewModel tests.
