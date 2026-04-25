# Portfolio Module Map

## WPF

- `PortfolioViewModel`
  - Screen-level state and command entry points.
- `PortfolioSellPanelController`
  - Sell panel preview and submission shaping.
- `PortfolioTradeDialogController`
  - Trade dialog open/edit state composition.
- `SubViewModels/AddAssetDialogViewModel`
  - Add/edit asset dialog state and orchestration.
- `SubViewModels/SellPanelViewModel`
  - Sell side-panel state and submission callbacks.
- `SubViewModels/TransactionDialogViewModel`
  - Shared transaction dialog state and routing.

## Application Services

- Load/query
  - `IPortfolioLoadService`
  - `IPortfolioHistoryQueryService`
  - `ILoanScheduleQueryService`
  - `IFinancialOverviewQueryService`
- Calculation
  - `IPortfolioSummaryService`
- Mutation/workflow
  - `IAddAssetWorkflowService`
  - `ITransactionWorkflowService`
  - `ISellWorkflowService`
  - `ITradeDeletionWorkflowService`
  - `ITradeMetadataWorkflowService`
  - `IPositionDeletionWorkflowService`
  - `IPositionMetadataWorkflowService`
  - `IAccountMutationWorkflowService`
  - `IAccountUpsertWorkflowService`
  - `ILoanPaymentWorkflowService`
  - `ILoanMutationWorkflowService`

## Infrastructure

- Repositories
  - Portfolio, trades, asset/accounts, loan schedules, alerts, history snapshots, position logs.
- Startup/data evolution
  - Schema migrators and bootstrap/startup tasks.

## Current Intent

- Keep ViewModels as thin UI coordinators.
- Keep workflow logic in `Assetra.Application`.
- Keep repository logic in `Assetra.Infrastructure`.
