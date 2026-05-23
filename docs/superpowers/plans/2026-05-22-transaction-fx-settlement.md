# Transaction FX Settlement Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make foreign-stock transaction entry explicit and auditable by separating trade currency, settlement cash currency, actual cash amount, FX rate, FX rate date, and FX source. A user buying US stocks through IB, Firstrade, or a Taiwan sub-broker must be able to record the exact cash paid while still keeping portfolio valuation and performance reports understandable in TWD.

**Architecture:** Keep transaction price and quantity in the instrument currency. Keep cash movement in the selected cash-account currency. Store FX metadata on the trade as audit context. Let actual cash amount be authoritative when the user provides it; use FX only to estimate or fill missing settlement values.

**Tech Stack:** .NET / WPF / MVVM CommunityToolkit, SQLite repositories, existing Assetra DesignSystem controls, existing FX history services, existing portfolio workflow services.

---

## Assumptions

- Instrument price remains in the instrument currency, for example USD 50.00 per share for DRAM.
- Cash account currency determines settlement currency, for example TWD for a Taiwan sub-broker account or USD for IB/Firstrade.
- `ActualCashAmount` is the most accurate value when the broker statement is known.
- FX rate is supporting metadata and an estimator, not a replacement for the user's actual cash amount.
- Valuation and performance reports may continue to present TWD values, but trade audit detail must preserve the original currencies.

## Success Criteria

- Taiwan-stock buy flow keeps working without showing unnecessary FX controls.
- US-stock buy flow shows instrument currency and settlement currency clearly.
- Cross-currency buy flow can fetch or manually enter FX, and can record actual cash paid.
- Saved trades preserve settlement currency, FX rate date, and FX source through reload, edit, sync mapping, and persistence tests.
- If FX cannot be fetched, the user receives a visible message and can still enter actual cash amount or manual FX.
- Existing transaction edit behavior does not duplicate trades.

## Phase 1 - Persist FX Settlement Metadata

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.Core/Models/Trade.cs`.
  - Add `SettlementCurrency`, defaulting to `TWD`.
  - Add nullable `FxRateDate`.
  - Add nullable `FxSource`.
  - Keep existing `InstrumentCurrency`, `CommissionCurrency`, and `FxRate`.

- [x] Update SQLite schema migration in `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs`.
  - Add `settlement_currency TEXT NOT NULL DEFAULT 'TWD'`.
  - Add `fx_rate_date TEXT NULL`.
  - Add `fx_source TEXT NULL`.
  - Existing rows should remain readable without destructive migration.

- [x] Update trade repository mapping in `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs`.
  - Write the new columns on insert/update.
  - Read the new columns on query.
  - Default blank or missing settlement currency to `TWD`.

- [x] Update sync mapper in `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Sync/TradeSyncMapper.cs`.
  - Include `SettlementCurrency`, `FxRateDate`, and `FxSource`.
  - Preserve backward compatibility for older payloads.

- [x] Add or update persistence tests.
  - `D:/Workspaces/Finances/Assetra/Assetra.Tests/Infrastructure/TradeSqliteRepositoryTests.cs`
  - `D:/Workspaces/Finances/Assetra/Assetra.Tests/Infrastructure/TradeSyncMapperTests.cs`
  - Verify new fields round-trip and old rows default safely.

## Phase 2 - Add Transaction FX Resolver

- [x] Add `D:/Workspaces/Finances/Assetra/Assetra.Application/Fx/TransactionFxRateResolver.cs`.
  - Input: trade date, instrument currency, settlement currency.
  - Output: rate, effective rate date, source, and status.
  - Same-currency pair returns rate `1` without calling external services.
  - Historical lookup should prefer the selected transaction date or the nearest available historical business date from existing FX history services.

- [x] Add a small DTO in `D:/Workspaces/Finances/Assetra/Assetra.Application/Fx/TransactionFxQuote.cs`.
  - Include `FromCurrency`, `ToCurrency`, `Rate`, `RateDate`, `Source`, and `IsEstimated`.

- [x] Use existing FX infrastructure first.
  - `D:/Workspaces/Finances/Assetra/Assetra.Application/Fx/IFxRateHistoryService.cs`
  - `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Fx/HybridFxRateProvider.cs`
  - Do not introduce a second FX provider path unless existing services cannot support the date lookup.

- [x] Add tests in `D:/Workspaces/Finances/Assetra/Assetra.Tests/Application/Fx/TransactionFxRateResolverTests.cs`.
  - Same currency returns `1`.
  - USD to TWD returns historical rate and date.
  - Missing rate returns a failure state, not a silent zero.

## Phase 3 - Wire Resolver Into Transaction Dialog

- [x] Update transaction dependencies.
  - Inspect and update the actual dependency record/class under `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/SubViewModels/`.
  - Register the resolver in application DI where other FX services are registered.

- [x] Extend buy context contracts.
  - Update `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/SubViewModels/Tx/IBuyExecutionContext.cs`.
  - Expose settlement currency, FX rate date, FX source, and whether FX was manually overridden.

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/SubViewModels/Tx/BuyTxViewModel.cs`.
  - Add `SettlementCurrency`.
  - Add `FxRateDate`.
  - Add `FxSourceLabel`.
  - Add `IsFxManual`.
  - Add `FxFetchError`.
  - Keep `ActualCashAmount` as the primary settlement amount field.

- [x] Add or update commands in transaction dialog VM partials.
  - Fetch FX when trade date, instrument currency, or cash account currency changes.
  - Allow manual override.
  - Do not overwrite a user-entered actual cash amount unless the user explicitly asks to recalculate.

- [x] Add VM tests in `D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/TransactionDialogViewModelTests.cs`.
  - Cross-currency buy requires actual cash amount or FX rate.
  - Auto FX fills rate metadata.
  - Manual FX survives confirm.
  - Existing edit-save path updates the original trade instead of duplicating.

## Phase 4 - Redesign Buy Transaction FX UI

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/Controls/TxForms/BuyTxForm.xaml`.
  - Replace the current cross-currency banner and advanced FX field with a compact `結算與匯率` section.
  - Show `成交幣別` and `扣款幣別`.
  - Show `實際扣款金額` as the primary cash settlement input.
  - Show `匯率`, `匯率日期`, `匯率來源`, and a refresh action.
  - Provide a manual override affordance.
  - Hide the section for same-currency trades unless a short read-only summary is useful.

- [x] Keep the market-price fetch separate from FX fetch.
  - Market price fills `成交價/股`.
  - FX fetch fills `匯率` and `匯率日期`.
  - The UI text must not imply that market price and FX come from the same source.

- [x] Apply existing DesignSystem styles.
  - Use shared button, input, helper text, validation, and dialog layout resources.
  - Avoid local one-off styles in the transaction form.

## Phase 5 - Persist Buy Workflow Values

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.Application/Portfolio/Services/AddAssetWorkflowDtos.cs`.
  - Add `SettlementCurrency`, `FxRateDate`, and `FxSource` to `StockBuyRequest`.

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Portfolio/SubViewModels/AddAssetDialogViewModel.cs`.
  - Parse and pass the new FX settlement fields.
  - Preserve existing actual cash inference behavior.
  - Do not infer a fake actual cash amount when neither actual cash nor FX is available.

- [x] Update `D:/Workspaces/Finances/Assetra/Assetra.Application/Portfolio/Services/AddAssetWorkflowService.cs`.
  - Save settlement currency, FX rate, FX rate date, and FX source on the created trade.
  - Continue to save price and quantity in instrument currency.

- [x] Update tests in `D:/Workspaces/Finances/Assetra/Assetra.Tests/Portfolio/AddAssetWorkflowServiceTests.cs`.
  - Verify actual cash amount is preserved.
  - Verify FX metadata is stored.
  - Verify same-currency buy does not require FX.

## Phase 6 - Edit, Side Panel, And Trade Detail

- [x] Update edit-loading logic in transaction dialog partials.
  - Loading an existing foreign trade should restore actual cash amount, FX rate, FX date, FX source, and settlement currency.
  - Saving should update the original trade.

- [x] Update the investment side-panel transaction list only if needed for clarity.
  - Show FX metadata for cross-currency trades in a compact secondary line.
  - Do not overload each card for same-currency trades.

- [x] Re-run existing side-panel edit tests or add a focused regression if missing.
  - Editing the same trade twice must not create a duplicate transaction.
  - Deleting a trade must refresh the UI list.

## Phase 7 - Reports And Documentation Boundary

- [x] Document behavior in `D:/Workspaces/Finances/Assetra/docs/`.
  - Trade entry stores original currency and settlement metadata.
  - Reports display TWD using available FX data and may be estimates when historical FX is missing.
  - Actual cash amount is the audit source of truth for cross-currency cash movement.

- [x] Do not broadly rewrite report calculations in this task.
  - Only update labels if they currently imply exactness where values are estimated.
  - A separate report accuracy task should handle realized PnL and performance methodology if needed.

## Verification Commands

- [x] Run focused tests:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore --filter "TransactionDialogViewModelTests|PortfolioViewModelTests|AddAssetWorkflowServiceTests|Fx|TradeSyncMapper"
```

- Result: Passed 230 focused tests.

- [x] Run full test suite if focused tests pass:

```powershell
dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj -c Debug --no-restore
```

- Result: Passed 1463 tests.

- [x] Build the WPF project:

```powershell
dotnet build D:\Workspaces\Finances\Assetra\Assetra.WPF\Assetra.WPF.csproj -c Debug --no-restore
```

- Result: Build succeeded with 0 warnings and 0 errors.

## Manual QA

- [ ] Taiwan stock + TWD cash account.
  - FX section is hidden or read-only.
  - Buy confirm works as before.

- [ ] US stock + USD cash account.
  - Instrument currency is USD.
  - Settlement currency is USD.
  - No unnecessary TWD FX requirement blocks save.

- [ ] US stock + TWD cash account.
  - Settlement section appears.
  - FX can be fetched for the selected trade date.
  - Actual cash amount can be entered directly from broker statement.
  - Saved trade reloads with the same FX metadata.

- [ ] FX provider unavailable.
  - UI shows a clear error.
  - User can still enter actual cash amount or manual FX.

- [ ] Edit regression.
  - Open an existing cross-currency trade.
  - Save twice.
  - Transaction list still has one edited record, not duplicates.
