# Application Layer Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 5 architectural cons in Assetra.Application in 3 phases: namespace unification, pass-through service removal, Execute-pattern standardization, PortfolioSummaryService migration to Core, and PortfolioViewModel sub-VM split.

**Architecture:** Incremental — Phase 1 (quick wins, zero business risk), Phase 2 (service layer contracts), Phase 3 (ViewModel decomposition). Each phase is independently buildable and testable.

**Tech Stack:** C# 13 / .NET 10, CommunityToolkit.Mvvm, WPF, xUnit + Moq

---

## File Map

### Phase 1

| Action | Path |
|--------|------|
| Rename namespace in all files | `Assetra.Application/**/*.cs`, `Assetra.WPF/**/*.cs`, `Assetra.Tests/**/*.cs` |
| Delete | `Assetra.Application/Portfolio/Contracts/IAlertQueryService.cs` |
| Delete | `Assetra.Application/Portfolio/Contracts/IAlertMutationService.cs` |
| Delete | `Assetra.Application/Portfolio/Services/AlertQueryService.cs` |
| Delete | `Assetra.Application/Portfolio/Services/AlertMutationService.cs` |
| Delete | `Assetra.Application/Portfolio/Contracts/ILoanScheduleQueryService.cs` |
| Delete | `Assetra.Application/Portfolio/Services/LoanScheduleQueryService.cs` |
| Modify | `Assetra.WPF/Features/Alerts/AlertsViewModel.cs` |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs` |
| Modify | `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs` |

### Phase 2

| Action | Path |
|--------|------|
| Modify | `Assetra.Application/Portfolio/Contracts/ITransactionWorkflowService.cs` |
| Modify | `Assetra.Application/Portfolio/Services/TransactionWorkflowService.cs` |
| Modify | `Assetra.Application/Portfolio/Services/LoanMutationWorkflowService.cs` |
| Modify | `Assetra.Application/Portfolio/Contracts/ILoanMutationWorkflowService.cs` |
| Delete | `Assetra.Application/Portfolio/Dtos/TransactionWorkflowPlan.cs` (plan record only; request records remain) |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Transactions.cs` |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs` |
| Modify | `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs` |
| Create | `Assetra.Core/DomainServices/IPortfolioSummaryService.cs` |
| Create | `Assetra.Core/DomainServices/PortfolioSummaryService.cs` |
| Create | `Assetra.Core/Dtos/PortfolioSummaryDtos.cs` |
| Delete | `Assetra.Application/Portfolio/Contracts/IPortfolioSummaryService.cs` |
| Delete | `Assetra.Application/Portfolio/Services/PortfolioSummaryService.cs` |
| Delete | `Assetra.Application/Portfolio/Dtos/PortfolioSummaryInput.cs` (move content to Core) |
| Delete | `Assetra.Application/Portfolio/Dtos/PortfolioSummaryResult.cs` (move content to Core) |

### Phase 3

| Action | Path |
|--------|------|
| Create | `Assetra.WPF/Features/Portfolio/SubViewModels/AddAssetDialogViewModel.cs` |
| Create | `Assetra.WPF/Features/Portfolio/SubViewModels/SellPanelViewModel.cs` |
| Create | `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs` |
| Create | `Assetra.WPF/Features/Portfolio/SubViewModels/AccountDialogViewModel.cs` |
| Create | `Assetra.WPF/Features/Portfolio/SubViewModels/LoanDialogViewModel.cs` |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs` |
| Delete | `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Assets.cs` |
| Delete | `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Transactions.cs` |
| Modify | `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs` |
| Modify | `Assetra.WPF/Features/Portfolio/PortfolioView.xaml` |
| Modify | `Assetra.Tests/WPF/PortfolioViewModelTests.cs` |

---

## Phase 1 — Quick Wins

---

### Task 1: Rename namespace Assetra.AppLayer → Assetra.Application

**Files:** All `.cs` in `Assetra.Application/`, `Assetra.WPF/`, `Assetra.Tests/`

- [ ] **Step 1: Verify current occurrences**

```bash
grep -r "Assetra\.AppLayer" Assetra.Application Assetra.WPF Assetra.Tests --include="*.cs" -l
```

Expected: several dozen files listed.

- [ ] **Step 2: Global rename via PowerShell**

```powershell
Get-ChildItem -Recurse -Include "*.cs" -Path "Assetra.Application","Assetra.WPF","Assetra.Tests" |
  ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    if ($content -match "Assetra\.AppLayer") {
      $content -replace "Assetra\.AppLayer", "Assetra.Application" |
        Set-Content $_.FullName -NoNewline
    }
  }
```

- [ ] **Step 3: Verify zero occurrences remain**

```bash
grep -r "Assetra\.AppLayer" Assetra.Application Assetra.WPF Assetra.Tests --include="*.cs"
```

Expected: no output.

- [ ] **Step 4: Build**

```bash
dotnet build Assetra.slnx
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Run tests**

```bash
dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename namespace Assetra.AppLayer to Assetra.Application"
```

---

### Task 2: Remove IAlertQueryService and IAlertMutationService

`AlertsViewModel` currently injects `IAlertQueryService` and `IAlertMutationService`, which are pass-throughs over `IAlertRepository`. We replace them with direct `IAlertRepository` injection.

**Files:**
- Delete: `Assetra.Application/Portfolio/Contracts/IAlertQueryService.cs`
- Delete: `Assetra.Application/Portfolio/Contracts/IAlertMutationService.cs`
- Delete: `Assetra.Application/Portfolio/Services/AlertQueryService.cs`
- Delete: `Assetra.Application/Portfolio/Services/AlertMutationService.cs`
- Modify: `Assetra.WPF/Features/Alerts/AlertsViewModel.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Open AlertsViewModel.cs and read its constructor + methods that use the two services**

Look for:
- `_alertQuery.GetAllAsync()` → replace with `_alertRepo.GetAllAsync()`
- `_alertMutation.AddAsync(rule)` → replace with `_alertRepo.AddAsync(rule)`
- `_alertMutation.UpdateAsync(rule)` → replace with `_alertRepo.UpdateAsync(rule)`
- `_alertMutation.RemoveAsync(id)` → replace with `_alertRepo.RemoveAsync(id)`

The exact method names may differ; check the file. Update the constructor to take `IAlertRepository alertRepo` and replace all call sites.

```csharp
// Before (constructor params)
IAlertQueryService alertQuery,
IAlertMutationService alertMutation,

// After
IAlertRepository alertRepo,
```

```csharp
// Before (field declarations)
private readonly IAlertQueryService _alertQuery;
private readonly IAlertMutationService _alertMutation;

// After
private readonly IAlertRepository _alertRepo;
```

- [ ] **Step 2: Delete the 4 Application files**

```bash
rm Assetra.Application/Portfolio/Contracts/IAlertQueryService.cs
rm Assetra.Application/Portfolio/Contracts/IAlertMutationService.cs
rm Assetra.Application/Portfolio/Services/AlertQueryService.cs
rm Assetra.Application/Portfolio/Services/AlertMutationService.cs
```

- [ ] **Step 3: Update DI in ServiceCollectionExtensions.cs**

In `AddAssetraApplicationServices`, remove:

```csharp
services.AddSingleton<IAlertQueryService>(sp =>
    new AlertQueryService(
        sp.GetRequiredService<IAlertRepository>()));
services.AddSingleton<IAlertMutationService>(sp =>
    new AlertMutationService(
        sp.GetRequiredService<IAlertRepository>()));
```

In `AddAssetraViewModels`, update `AlertsViewModel` registration:

```csharp
// Before
services.AddSingleton<AlertsViewModel>(sp => new AlertsViewModel(
    sp.GetRequiredService<IAlertQueryService>(),
    sp.GetRequiredService<IAlertMutationService>(),
    sp.GetRequiredService<IStockSearchService>(),
    ...));

// After
services.AddSingleton<AlertsViewModel>(sp => new AlertsViewModel(
    sp.GetRequiredService<IAlertRepository>(),
    sp.GetRequiredService<IStockSearchService>(),
    ...));
```

- [ ] **Step 4: Remove using statements for the deleted interfaces**

Remove `using Assetra.Application.Portfolio.Contracts;` from `AlertsViewModel.cs` if it's only there for the two deleted interfaces. Add `using Assetra.Core.Interfaces;` if not already present.

- [ ] **Step 5: Build and test**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: remove IAlertQueryService/IAlertMutationService pass-through services"
```

---

### Task 3: Remove ILoanScheduleQueryService

`PortfolioViewModel` already has `ILoanScheduleRepository` via `PortfolioRepositories.LoanSchedule`. `ILoanScheduleQueryService` (in `PortfolioServices`) is a redundant pass-through.

**Files:**
- Delete: `Assetra.Application/Portfolio/Contracts/ILoanScheduleQueryService.cs`
- Delete: `Assetra.Application/Portfolio/Services/LoanScheduleQueryService.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Find all usages of _loanScheduleQueryService in PortfolioViewModel**

```bash
grep -n "_loanScheduleQueryService\|LoanScheduleQuery" Assetra.WPF/Features/Portfolio/PortfolioViewModel*.cs
```

Note each call site. The typical call is `await _loanScheduleQueryService.GetByAssetAsync(assetId, ct)`. Replace with `await _loanScheduleRepo.GetByAssetAsync(assetId, ct)` (`_loanScheduleRepo` is already assigned from `PortfolioRepositories.LoanSchedule`).

- [ ] **Step 2: Remove LoanScheduleQuery from PortfolioServices record**

In `PortfolioDependencies.cs`, remove:

```csharp
ILoanScheduleQueryService? LoanScheduleQuery = null,
```

- [ ] **Step 3: Update PortfolioViewModel.cs constructor assignment**

Remove the line that assigns `_loanScheduleQueryService = services.LoanScheduleQuery ?? ...` and its field declaration `private readonly ILoanScheduleQueryService _loanScheduleQueryService;`.

Confirm `_loanScheduleRepo` is already set from `PortfolioRepositories.LoanSchedule` and use it.

- [ ] **Step 4: Delete Application files**

```bash
rm Assetra.Application/Portfolio/Contracts/ILoanScheduleQueryService.cs
rm Assetra.Application/Portfolio/Services/LoanScheduleQueryService.cs
```

- [ ] **Step 5: Update DI in ServiceCollectionExtensions.cs**

Remove from `AddAssetraApplicationServices`:

```csharp
services.AddSingleton<ILoanScheduleQueryService>(sp =>
    new LoanScheduleQueryService(
        sp.GetRequiredService<ILoanScheduleRepository>()));
```

Remove `LoanScheduleQuery: sp.GetRequiredService<ILoanScheduleQueryService>()` from the `PortfolioServices(...)` constructor call in `AddAssetraViewModels`.

- [ ] **Step 6: Build and test**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: 0 errors, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: remove ILoanScheduleQueryService pass-through, use ILoanScheduleRepository directly"
```

---

## Phase 2 — Structural Adjustments

---

### Task 4: Convert TransactionWorkflowService to Execute pattern

Currently `ITransactionWorkflowService` returns `TransactionWorkflowPlan` and callers loop over the plan persisting each trade. We move persistence into the service so callers just call `await _txWorkflow.Record*Async(request, ct)`.

`LoanMutationWorkflowService` also uses `ITransactionWorkflowService.CreateLoanPlan()`. We inline that logic into `LoanMutationWorkflowService` directly so it no longer depends on `ITransactionWorkflowService`.

**Files:**
- Modify: `Assetra.Application/Portfolio/Contracts/ITransactionWorkflowService.cs`
- Modify: `Assetra.Application/Portfolio/Services/TransactionWorkflowService.cs`
- Modify: `Assetra.Application/Portfolio/Services/LoanMutationWorkflowService.cs`
- Modify: `Assetra.Application/Portfolio/Contracts/ILoanMutationWorkflowService.cs`
- Modify: `Assetra.Application/Portfolio/Dtos/TransactionWorkflowPlan.cs` (remove plan record, keep request records)
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Transactions.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Write failing test for RecordIncomeAsync**

In `Assetra.Tests/Application/TransactionWorkflowServiceTests.cs` (create file):

```csharp
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application;

public sealed class TransactionWorkflowServiceTests
{
    [Fact]
    public async Task RecordIncomeAsync_RecordsTradeThroughTransactionService()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var request = new IncomeTransactionRequest(
            Amount: 5000m,
            TradeDate: new DateTime(2026, 1, 1),
            CashAccountId: null,
            Note: "薪資",
            Fee: 0m);

        await sut.RecordIncomeAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Income && t.CashAmount == 5000m)),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "TransactionWorkflowServiceTests"
```

Expected: compilation error — `TransactionWorkflowService` has no constructor taking `ITransactionService`.

- [ ] **Step 3: Update ITransactionWorkflowService.cs**

```csharp
using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ITransactionWorkflowService
{
    Task RecordCashDividendAsync(CashDividendTransactionRequest request, CancellationToken ct = default);
    Task RecordStockDividendAsync(StockDividendTransactionRequest request, CancellationToken ct = default);
    Task RecordIncomeAsync(IncomeTransactionRequest request, CancellationToken ct = default);
    Task RecordCashFlowAsync(CashFlowTransactionRequest request, CancellationToken ct = default);
    Task RecordTransferAsync(TransferTransactionRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 4: Update TransactionWorkflowService.cs**

Add `ITransactionService` injection and convert each method:

```csharp
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Services;

namespace Assetra.Application.Portfolio.Services;

public sealed class TransactionWorkflowService : ITransactionWorkflowService
{
    private readonly ITransactionService _txService;

    public TransactionWorkflowService(ITransactionService txService)
    {
        _txService = txService;
    }

    public async Task RecordCashDividendAsync(CashDividendTransactionRequest request, CancellationToken ct = default)
    {
        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.Symbol,
            Exchange: request.Exchange,
            Name: request.Name,
            Type: TradeType.CashDividend,
            TradeDate: request.TradeDate,
            Price: request.PerShare,
            Quantity: request.Quantity,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.TotalAmount,
            CashAccountId: request.CashAccountId,
            Note: null);
        foreach (var trade in BuildTrades(mainTrade, request.Fee, request.CashAccountId,
                     request.TradeDate, $"{request.Name} 股息手續費", null))
        {
            ct.ThrowIfCancellationRequested();
            await _txService.RecordAsync(trade).ConfigureAwait(false);
        }
    }

    public async Task RecordStockDividendAsync(StockDividendTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.Symbol,
            Exchange: request.Exchange,
            Name: request.Name,
            Type: TradeType.StockDividend,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: request.NewShares,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: null,
            CashAccountId: null,
            Note: null,
            PortfolioEntryId: request.PortfolioEntryId);
        await _txService.RecordAsync(trade).ConfigureAwait(false);
    }

    public async Task RecordIncomeAsync(IncomeTransactionRequest request, CancellationToken ct = default)
    {
        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: request.Note,
            Type: TradeType.Income,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: request.Note);
        foreach (var trade in BuildTrades(mainTrade, request.Fee, request.CashAccountId,
                     request.TradeDate, $"{request.Note} 手續費", null))
        {
            ct.ThrowIfCancellationRequested();
            await _txService.RecordAsync(trade).ConfigureAwait(false);
        }
    }

    public async Task RecordCashFlowAsync(CashFlowTransactionRequest request, CancellationToken ct = default)
    {
        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.AccountName,
            Exchange: string.Empty,
            Name: request.AccountName,
            Type: request.Type,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: request.Note);
        foreach (var trade in BuildTrades(mainTrade, request.Fee, request.CashAccountId,
                     request.TradeDate, $"{request.AccountName} 手續費", request.Note))
        {
            ct.ThrowIfCancellationRequested();
            await _txService.RecordAsync(trade).ConfigureAwait(false);
        }
    }

    public async Task RecordTransferAsync(TransferTransactionRequest request, CancellationToken ct = default)
    {
        var trades = new List<Trade>();
        Guid feeParentId;

        if (request.SourceAmount == request.DestinationAmount)
        {
            var transfer = new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.SourceName,
                Exchange: string.Empty,
                Name: $"{request.SourceName} → {request.DestinationName}",
                Type: TradeType.Transfer,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.SourceAmount,
                CashAccountId: request.SourceCashAccountId,
                Note: request.Note,
                ToCashAccountId: request.DestinationCashAccountId);
            trades.Add(transfer);
            feeParentId = transfer.Id;
        }
        else
        {
            var withdrawNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"轉帳 → {request.DestinationName}"
                : $"轉帳 → {request.DestinationName} — {request.Note}";
            var withdraw = new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.SourceName,
                Exchange: string.Empty,
                Name: request.SourceName,
                Type: TradeType.Withdrawal,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.SourceAmount,
                CashAccountId: request.SourceCashAccountId,
                Note: withdrawNote);
            trades.Add(withdraw);
            feeParentId = withdraw.Id;

            var depositNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"轉帳 ← {request.SourceName}"
                : $"轉帳 ← {request.SourceName} — {request.Note}";
            trades.Add(new Trade(
                Id: Guid.NewGuid(),
                Symbol: request.DestinationName,
                Exchange: string.Empty,
                Name: request.DestinationName,
                Type: TradeType.Deposit,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.DestinationAmount,
                CashAccountId: request.DestinationCashAccountId,
                Note: depositNote));
        }

        if (request.Fee > 0)
            trades.Add(CreateFeeTrade(request.Fee, request.SourceCashAccountId, request.TradeDate,
                $"轉帳手續費 ({request.SourceName} → {request.DestinationName})", request.Note, feeParentId));

        foreach (var trade in trades)
        {
            ct.ThrowIfCancellationRequested();
            await _txService.RecordAsync(trade).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<Trade> BuildTrades(Trade mainTrade, decimal fee, Guid? cashAccountId,
        DateTime tradeDate, string notePrefix, string? userNote)
    {
        var trades = new List<Trade> { mainTrade };
        if (fee > 0)
            trades.Add(CreateFeeTrade(fee, cashAccountId, tradeDate, notePrefix, userNote, mainTrade.Id));
        return trades;
    }

    private static Trade CreateFeeTrade(decimal fee, Guid? cashAccountId, DateTime tradeDate,
        string notePrefix, string? userNote, Guid parentTradeId)
    {
        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: "手續費",
            Type: TradeType.Withdrawal,
            TradeDate: tradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: fee,
            CashAccountId: cashAccountId,
            Note: string.IsNullOrWhiteSpace(userNote) ? notePrefix : $"{notePrefix} — {userNote}",
            ParentTradeId: parentTradeId);
    }
}
```

- [ ] **Step 5: Update LoanMutationWorkflowService.cs — remove ITransactionWorkflowService dependency, inline loan trade building**

`LoanMutationWorkflowService` previously called `_transactionWorkflowService.CreateLoanPlan()` to build loan trades. Inline the trade-building logic directly:

```csharp
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Services;

namespace Assetra.Application.Portfolio.Services;

public sealed class LoanMutationWorkflowService : ILoanMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ILoanScheduleRepository _loanScheduleRepository;
    private readonly ITransactionService _transactionService;

    public LoanMutationWorkflowService(
        IAssetRepository assetRepository,
        ILoanScheduleRepository loanScheduleRepository,
        ITransactionService transactionService)
    {
        _assetRepository = assetRepository;
        _loanScheduleRepository = loanScheduleRepository;
        _transactionService = transactionService;
    }

    public async Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        AssetItem? liabilityAsset = null;
        IReadOnlyList<LoanScheduleEntry>? scheduleEntries = null;

        if (request.Type == TradeType.LoanBorrow &&
            request.AmortAnnualRate.HasValue &&
            request.AmortTermMonths.HasValue &&
            request.FirstPaymentDate.HasValue)
        {
            liabilityAsset = new AssetItem(
                Guid.NewGuid(),
                request.LoanLabel,
                FinancialType.Liability,
                null,
                "TWD",
                DateOnly.FromDateTime(DateTime.Today),
                IsActive: true,
                UpdatedAt: null,
                LoanAnnualRate: request.AmortAnnualRate,
                LoanTermMonths: request.AmortTermMonths,
                LoanStartDate: request.FirstPaymentDate,
                LoanHandlingFee: request.Fee > 0 ? request.Fee : null);
            scheduleEntries = AmortizationService.Generate(
                liabilityAsset.Id,
                request.CashAmount,
                request.AmortAnnualRate.Value,
                request.AmortTermMonths.Value,
                request.FirstPaymentDate.Value);
        }

        if (liabilityAsset is not null)
            await _assetRepository.AddItemAsync(liabilityAsset).ConfigureAwait(false);
        if (scheduleEntries is not null)
            await _loanScheduleRepository.BulkInsertAsync(scheduleEntries).ConfigureAwait(false);

        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.LoanLabel,
            Exchange: string.Empty,
            Name: request.LoanLabel,
            Type: request.Type,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.CashAmount,
            CashAccountId: request.CashAccountId,
            Note: request.Note,
            LoanLabel: request.LoanLabel,
            Principal: request.Principal,
            InterestPaid: request.InterestPaid);

        var trades = new List<Trade> { mainTrade };
        if (request.Fee > 0)
            trades.Add(new Trade(
                Id: Guid.NewGuid(),
                Symbol: string.Empty,
                Exchange: string.Empty,
                Name: "手續費",
                Type: TradeType.Withdrawal,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.Fee,
                CashAccountId: request.CashAccountId,
                Note: $"{request.LoanLabel} 手續費",
                ParentTradeId: mainTrade.Id));

        foreach (var trade in trades)
        {
            ct.ThrowIfCancellationRequested();
            await _transactionService.RecordAsync(trade).ConfigureAwait(false);
        }

        return new LoanMutationResult(liabilityAsset?.Id, scheduleEntries);
    }
}
```

Also create the result record at the bottom of `LoanMutationWorkflowService.cs`:

```csharp
public sealed record LoanMutationResult(Guid? LiabilityAssetId, IReadOnlyList<LoanScheduleEntry>? ScheduleEntries);
```

Update `ILoanMutationWorkflowService.cs` return type accordingly:

```csharp
public interface ILoanMutationWorkflowService
{
    Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 6: Remove TransactionWorkflowPlan record from Dtos**

In `TransactionWorkflowPlan.cs`, delete only the `TransactionWorkflowPlan` record (lines 5–8). Keep all `*TransactionRequest` records in that file.

After deletion the file should start with the first request record.

- [ ] **Step 7: Update PortfolioViewModel.Transactions.cs call sites**

Find all `Create*Plan` calls and `foreach (var trade in plan.Trades)` loops. Replace each block with a single `await _transactionWorkflowService.Record*Async(request, ct)` call.

Example — income:

```csharp
// Before
var plan = _transactionWorkflowService.CreateIncomePlan(request);
foreach (var trade in plan.Trades)
    await _txService.RecordAsync(trade);

// After
await _transactionWorkflowService.RecordIncomeAsync(request, ct);
```

Do the same for cash dividend, stock dividend, cash flow, and transfer.

For loan, the ViewModel calls `_loanMutationWorkflowService.RecordAsync(request, ct)` which already handles persistence. Check the ViewModel's loan recording code and update it to use the new `LoanMutationResult` return type if needed (e.g., the liability asset ID for UI refresh).

- [ ] **Step 8: Remove ITransactionService from PortfolioServices**

In `PortfolioDependencies.cs`, remove:

```csharp
ITransactionService? Transaction = null,
```

In `PortfolioViewModel.cs`, remove the `_txService` field declaration and its assignment from the constructor. Remove all uses of `_txService` (there should be none left after Step 7).

- [ ] **Step 9: Update DI registration for TransactionWorkflowService**

In `ServiceCollectionExtensions.cs`, update:

```csharp
// Before
services.AddSingleton<ITransactionWorkflowService, TransactionWorkflowService>();

// After
services.AddSingleton<ITransactionWorkflowService>(sp =>
    new TransactionWorkflowService(
        sp.GetRequiredService<ITransactionService>()));
```

Update `ILoanMutationWorkflowService` registration — remove `ITransactionWorkflowService` dependency:

```csharp
services.AddSingleton<ILoanMutationWorkflowService>(sp =>
    new LoanMutationWorkflowService(
        sp.GetRequiredService<IAssetRepository>(),
        sp.GetRequiredService<ILoanScheduleRepository>(),
        sp.GetRequiredService<ITransactionService>()));
```

Remove `Transaction: sp.GetRequiredService<ITransactionService>()` from the `PortfolioServices(...)` constructor call.

- [ ] **Step 10: Run tests**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: new `TransactionWorkflowServiceTests` passes; all existing tests pass.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "refactor: convert TransactionWorkflowService to Execute pattern, inline loan logic in LoanMutationWorkflowService"
```

---

### Task 5: Move PortfolioSummaryService to Core.DomainServices

`PortfolioSummaryService` is a pure calculation (no IO) — it belongs in `Assetra.Core`.

**Files:**
- Create: `Assetra.Core/DomainServices/IPortfolioSummaryService.cs`
- Create: `Assetra.Core/DomainServices/PortfolioSummaryService.cs`
- Create: `Assetra.Core/Dtos/PortfolioSummaryDtos.cs`
- Delete: `Assetra.Application/Portfolio/Contracts/IPortfolioSummaryService.cs`
- Delete: `Assetra.Application/Portfolio/Services/PortfolioSummaryService.cs`
- Delete: `Assetra.Application/Portfolio/Dtos/PortfolioSummaryInput.cs`
- Delete: `Assetra.Application/Portfolio/Dtos/PortfolioSummaryResult.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` (using update)

- [ ] **Step 1: Create Core/Dtos/PortfolioSummaryDtos.cs**

Copy the content from `Assetra.Application/Portfolio/Dtos/PortfolioSummaryInput.cs` and `PortfolioSummaryResult.cs` into a single file, changing namespace:

```csharp
using Assetra.Core.Models;

namespace Assetra.Core.Dtos;

public sealed record PortfolioSummaryInput(
    IReadOnlyList<PositionSummaryInput> Positions,
    IReadOnlyList<CashBalanceInput> CashBalances,
    IReadOnlyList<LiabilityBalanceInput> LiabilityBalances,
    decimal MonthlyExpense);

public sealed record PositionSummaryInput(
    Guid EntryId,
    decimal Quantity,
    decimal AvgCost,
    decimal CurrentPrice,
    decimal PrevClosePrice,
    bool IsPriceLoaded);

public sealed record CashBalanceInput(Guid AccountId, decimal Balance);

public sealed record LiabilityBalanceInput(Guid AssetId, decimal Balance, decimal OriginalAmount);

public sealed record PortfolioSummaryResult(
    decimal TotalCost,
    decimal TotalMarketValue,
    decimal TotalPnl,
    decimal TotalPnlPercent,
    decimal TotalCash,
    decimal TotalLiabilities,
    decimal TotalOriginalLiabilities,
    decimal TotalAssets,
    decimal NetWorth,
    decimal DayPnl,
    decimal DayPnlPercent,
    decimal TotalRealizedPnl,
    decimal TotalIncome,
    decimal TotalDividends,
    decimal DebtRatio,
    decimal PaidPercent,
    decimal EmergencyFundMonths,
    IReadOnlyList<PositionWeightResult> PositionWeights,
    IReadOnlyList<AllocationSliceResult> AllocationSlices);

public sealed record PositionWeightResult(Guid EntryId, decimal WeightPercent);

public sealed record AllocationSliceResult(
    AllocationSliceKind Kind,
    decimal Value,
    decimal Percent,
    AssetType? AssetType = null);

public enum AllocationSliceKind { AssetType, Cash, Liabilities }
```

Verify the exact field names match the existing Application-layer DTOs by reading `Assetra.Application/Portfolio/Dtos/PortfolioSummaryInput.cs` and `PortfolioSummaryResult.cs` before finalizing.

- [ ] **Step 2: Create Core/DomainServices/IPortfolioSummaryService.cs**

```csharp
using Assetra.Core.Dtos;

namespace Assetra.Core.DomainServices;

public interface IPortfolioSummaryService
{
    PortfolioSummaryResult Calculate(PortfolioSummaryInput input);
}
```

- [ ] **Step 3: Create Core/DomainServices/PortfolioSummaryService.cs**

Copy the full implementation from `Assetra.Application/Portfolio/Services/PortfolioSummaryService.cs`, changing:
- namespace: `Assetra.Core.DomainServices`
- all `using Assetra.Application.*` → `using Assetra.Core.Dtos`

The logic itself is unchanged.

- [ ] **Step 4: Delete Application-layer files**

```bash
rm Assetra.Application/Portfolio/Contracts/IPortfolioSummaryService.cs
rm Assetra.Application/Portfolio/Services/PortfolioSummaryService.cs
rm Assetra.Application/Portfolio/Dtos/PortfolioSummaryInput.cs
rm Assetra.Application/Portfolio/Dtos/PortfolioSummaryResult.cs
```

- [ ] **Step 5: Update using statements in PortfolioViewModel.cs**

Replace:
```csharp
using Assetra.Application.Portfolio.Contracts; // IPortfolioSummaryService
using Assetra.Application.Portfolio.Dtos;      // PortfolioSummaryInput, etc.
```
With:
```csharp
using Assetra.Core.DomainServices;
using Assetra.Core.Dtos;
```

Do the same for any other file that imported those types.

- [ ] **Step 6: Update DI registration**

In `ServiceCollectionExtensions.cs`, move the summary service registration from `AddAssetraApplicationServices` to `AddAssetraDataServices` (or create an `AddAssetraCoreServices` group):

```csharp
services.AddSingleton<IPortfolioSummaryService, PortfolioSummaryService>();
```

Using: `using Assetra.Core.DomainServices;`

Remove the old registration from `AddAssetraApplicationServices`.

- [ ] **Step 7: Build and test**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: 0 errors, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: move PortfolioSummaryService to Core.DomainServices"
```

---

## Phase 3 — ViewModel Decomposition

**Strategy:** Extract one Sub-VM at a time. Each Sub-VM:
1. Owns its own `[ObservableProperty]` state and `[RelayCommand]` methods
2. Accepts only the services it needs via constructor
3. Notifies the parent VM of completed operations via `event` or `Action` callback
4. Is registered in DI and injected into `PortfolioViewModel`

XAML binding changes: `{Binding PropertyName}` → `{Binding SubVmName.PropertyName}` for all moved properties.

---

### Task 6: Create AddAssetDialogViewModel

Extract the "add new asset" dialog state and commands from `PortfolioViewModel.Assets.cs`.

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/AddAssetDialogViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioView.xaml`

- [ ] **Step 1: Identify all state and commands to move**

In `PortfolioViewModel.Assets.cs`, locate these categories (line ranges are approximate — verify by reading the file):
- Add dialog visibility + type: `_isAddDialogOpen`, `_addDialogIsInvestmentMode`, `_addAssetType`
- Stock fields: `_addBuyDate`, `_addSymbol`, `_addPrice`, `_addQuantity`, `_addError`, and all `_add*Error` validation fields
- Close price loading: `_isLoadingClosePrice`, `_closePriceHint`
- Buy preview: `_addGrossAmount`, `_addCommission`, `_addTotalCost`, `_addCostPerShare`
- Suggestions: `_isSuggestionsOpen`, `_selectedSuggestion`, `_symbolSuggestions`
- Manual asset fields: `_addName`, `_addCost`
- Crypto fields: `_addCryptoSymbol`, `_addCryptoQty`, `_addCryptoPrice`
- Cash account field: `_addAccountName`
- Commands: `CloseAddDialog`, `ConfirmAdd`, `AddPosition`, and related `On*Changed` partials

- [ ] **Step 2: Create AddAssetDialogViewModel.cs**

```csharp
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public partial class AddAssetDialogViewModel : ObservableObject
{
    private readonly IAddAssetWorkflowService _addAssetWorkflow;
    private readonly ISnackbarService? _snackbar;

    /// <summary>Fired after a successful buy/add so the parent VM can reload positions.</summary>
    public event EventHandler? AssetAdded;

    public AddAssetDialogViewModel(IAddAssetWorkflowService addAssetWorkflow, ISnackbarService? snackbar = null)
    {
        _addAssetWorkflow = addAssetWorkflow;
        _snackbar = snackbar;
    }

    // ── Paste all [ObservableProperty] fields from PortfolioViewModel.Assets.cs ──
    // ── that belong to the add-dialog (IsAddDialogOpen, AddSymbol, AddPrice, etc.) ──
    // ── Paste all [RelayCommand] methods: CloseAddDialog, ConfirmAdd, AddPosition, etc. ──
    // ── Replace _addAssetWorkflowService with _addAssetWorkflow ──
    // ── Replace _snackbar? calls — already correct ──
    // ── On success, raise AssetAdded: AssetAdded?.Invoke(this, EventArgs.Empty); ──
}
```

Copy the identified state and commands verbatim from `Assets.cs`. At the end of each successful add/buy, add:

```csharp
AssetAdded?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 3: Wire into PortfolioViewModel**

In `PortfolioViewModel.cs` constructor:

```csharp
AddAssetDialog = new AddAssetDialogViewModel(...);
AddAssetDialog.AssetAdded += async (_, _) => await ReloadAfterAssetChange();
```

Expose as a public property:

```csharp
public AddAssetDialogViewModel AddAssetDialog { get; }
```

- [ ] **Step 4: Update XAML bindings in PortfolioView.xaml**

Search for all bindings that reference the moved properties. Change:

```xml
<!-- Before -->
{Binding IsAddDialogOpen}
{Binding AddSymbol}
{Binding AddPrice}
<!-- ... etc -->

<!-- After -->
{Binding AddAssetDialog.IsAddDialogOpen}
{Binding AddAssetDialog.AddSymbol}
{Binding AddAssetDialog.AddPrice}
<!-- ... etc -->
```

Also update `Command="{Binding CloseAddDialogCommand}"` → `Command="{Binding AddAssetDialog.CloseAddDialogCommand}"`.

Use `grep -n "AddSymbol\|AddPrice\|AddQuantity\|IsAddDialogOpen\|AddDialogIs\|AddAssetType\|ConfirmAdd\|CloseAddDialog\|AddPosition" Assetra.WPF/Features/Portfolio/PortfolioView.xaml` to find all occurrences.

- [ ] **Step 5: Register in DI**

In `ServiceCollectionExtensions.cs`, `AddAssetraViewModels`:

```csharp
services.AddSingleton<AddAssetDialogViewModel>(sp => new AddAssetDialogViewModel(
    sp.GetRequiredService<IAddAssetWorkflowService>(),
    sp.GetRequiredService<ISnackbarService>()));
```

Inject into `PortfolioViewModel` constructor via `PortfolioServices` or as a standalone param — choose whichever causes less DI churn. The simplest: add `AddAssetDialogViewModel? AddAssetDialog = null` to `PortfolioServices` record.

- [ ] **Step 6: Remove moved code from Assets.cs**

Delete the add-dialog state and commands from `PortfolioViewModel.Assets.cs` after confirming the Sub-VM works. Leave sell, edit-asset, and account-management code in place for now.

- [ ] **Step 7: Build, run app and test add-asset flow manually**

```bash
dotnet build Assetra.slnx
```

Run the WPF app, open the add-asset dialog, add a stock, verify the position appears. Also verify existing tests pass:

```bash
dotnet test Assetra.Tests/Assetra.Tests.csproj
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: extract AddAssetDialogViewModel from PortfolioViewModel"
```

---

### Task 7: Create SellPanelViewModel

Extract sell-panel state and commands from `PortfolioViewModel.Assets.cs`.

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/SellPanelViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioView.xaml`

- [ ] **Step 1: Identify sell-panel state in Assets.cs**

Properties to move (approximate lines 232–246 + related commands):
- `_sellingRow`, `_isSellPanelVisible`, `_sellPriceInput`, `_sellPanelError`, `_isSellEtf`
- `_sellGrossAmount`, `_sellCommission`, `_sellTransactionTax`, `_sellNetAmount`
- `_sellEstimatedPnl`, `_isSellEstimatedPositive`
- Commands: `BeginSell`, `BeginSellForSelectedPosition`, `CancelSell`, `ConfirmSell`

- [ ] **Step 2: Create SellPanelViewModel.cs**

```csharp
using Assetra.Application.Portfolio.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public partial class SellPanelViewModel : ObservableObject
{
    private readonly ISellWorkflowService _sellWorkflow;
    private readonly ISnackbarService? _snackbar;

    public event EventHandler<SellCompletedEventArgs>? SellCompleted;

    public SellPanelViewModel(ISellWorkflowService sellWorkflow, ISnackbarService? snackbar = null)
    {
        _sellWorkflow = sellWorkflow;
        _snackbar = snackbar;
    }

    // ── Paste sell panel [ObservableProperty] fields ──
    // ── Paste BeginSell, CancelSell, ConfirmSell commands ──
    // ── On ConfirmSell success: SellCompleted?.Invoke(this, new SellCompletedEventArgs(result)); ──
}

public sealed record SellCompletedEventArgs(string Symbol, string Exchange, Guid EntryId);
```

- [ ] **Step 3: Wire into PortfolioViewModel, update XAML, register in DI**

Same pattern as Task 6:
1. `SellPanel = new SellPanelViewModel(...);`
2. Subscribe to `SellPanel.SellCompleted` to reload positions/trades
3. Update XAML: `{Binding SellingRow}` → `{Binding SellPanel.SellingRow}` etc.
4. Add to DI

Use `grep -n "SellingRow\|IsSellPanelVisible\|SellPriceInput\|BeginSell\|CancelSell\|ConfirmSell\|SellGrossAmount" Assetra.WPF/Features/Portfolio/PortfolioView.xaml` to find bindings.

- [ ] **Step 4: Remove from Assets.cs, build, test manually, commit**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
git add -A
git commit -m "refactor: extract SellPanelViewModel from PortfolioViewModel"
```

---

### Task 8: Create TransactionDialogViewModel

Extract transaction dialog state and commands from `PortfolioViewModel.Transactions.cs`.

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioView.xaml`
- Delete: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Transactions.cs` (after complete extraction)

- [ ] **Step 1: Identify state in Transactions.cs**

Read `PortfolioViewModel.Transactions.cs` in full. Collect all:
- Dialog open/type state: `_isTxDialogOpen`, `_txType`, `_editingTradeId`
- Input fields: `_txAmount`, `_txNote`, `_txDate`, `_txError`, all `_tx*Error` validation fields
- Cash account selection: `_txCashAccount`
- All income, dividend, cash flow, sell, transfer input fields
- Commands: `ConfirmTx`, `CloseTxDialog`, `OpenTxDialog`, etc.

- [ ] **Step 2: Create TransactionDialogViewModel.cs**

```csharp
using Assetra.Application.Portfolio.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public partial class TransactionDialogViewModel : ObservableObject
{
    private readonly ITransactionWorkflowService _transactionWorkflow;
    private readonly ITradeDeletionWorkflowService _tradeDeletion;
    private readonly ITradeMetadataWorkflowService _tradeMetadata;
    private readonly ISnackbarService? _snackbar;

    public event EventHandler? TransactionCompleted;

    public TransactionDialogViewModel(
        ITransactionWorkflowService transactionWorkflow,
        ITradeDeletionWorkflowService tradeDeletion,
        ITradeMetadataWorkflowService tradeMetadata,
        ISnackbarService? snackbar = null)
    {
        _transactionWorkflow = transactionWorkflow;
        _tradeDeletion = tradeDeletion;
        _tradeMetadata = tradeMetadata;
        _snackbar = snackbar;
    }

    // ── Paste all transaction-dialog [ObservableProperty] and [RelayCommand] members ──
    // ── On successful transaction, raise TransactionCompleted ──
}
```

Note: `PortfolioViewModel.Transactions.cs` currently also holds `ConfirmSell` and sell-dialog state — those were moved to `SellPanelViewModel` in Task 7. Only move the remaining transaction-dialog code.

- [ ] **Step 3: Wire, update XAML, register in DI, delete Transactions.cs, commit**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
git add -A
git commit -m "refactor: extract TransactionDialogViewModel from PortfolioViewModel"
```

---

### Task 9: Create AccountDialogViewModel and LoanDialogViewModel

Extract account and loan dialog state from `PortfolioViewModel.Assets.cs`.

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/AccountDialogViewModel.cs`
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/LoanDialogViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioView.xaml`
- Delete: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Assets.cs` (after all code extracted)

- [ ] **Step 1: AccountDialogViewModel — identify state**

From Assets.cs, collect account management state:
- `_addAccountName`
- `_defaultCashAccountId`, `_showArchivedAccounts`
- Edit-account fields: `_isEditAssetDialogOpen`, `_editAssetName`, `_editAssetTypeLabel`, etc.
- Commands: `OpenAddAccountDialog`, `ArchiveAccount`, `RemoveCashAccount`, `SetAsDefaultCashAccount`, `ClearDefaultCashAccount`, `ToggleDefaultCashAccount`, `OpenEditCash`, `OpenEditPosition`, `CloseEditAsset`, `SaveEditAsset`

- [ ] **Step 2: Create AccountDialogViewModel.cs**

```csharp
using Assetra.Application.Portfolio.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public partial class AccountDialogViewModel : ObservableObject
{
    private readonly IAccountUpsertWorkflowService _accountUpsert;
    private readonly IAccountMutationWorkflowService _accountMutation;
    private readonly ISnackbarService? _snackbar;

    public event EventHandler? AccountChanged;

    public AccountDialogViewModel(
        IAccountUpsertWorkflowService accountUpsert,
        IAccountMutationWorkflowService accountMutation,
        ISnackbarService? snackbar = null)
    {
        _accountUpsert = accountUpsert;
        _accountMutation = accountMutation;
        _snackbar = snackbar;
    }

    // ── Paste account dialog [ObservableProperty] and [RelayCommand] members ──
    // ── On success: AccountChanged?.Invoke(this, EventArgs.Empty); ──
}
```

- [ ] **Step 3: LoanDialogViewModel — identify state and create**

Loan dialog state (from Assets.cs and main PortfolioViewModel.cs — check grep):

```bash
grep -n "Loan\|loan" Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs | head -40
```

```csharp
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public partial class LoanDialogViewModel : ObservableObject
{
    private readonly ILoanMutationWorkflowService _loanMutation;
    private readonly ILoanPaymentWorkflowService _loanPayment;
    private readonly ILoanScheduleRepository _loanScheduleRepo;
    private readonly ISnackbarService? _snackbar;

    public event EventHandler? LoanChanged;

    public LoanDialogViewModel(
        ILoanMutationWorkflowService loanMutation,
        ILoanPaymentWorkflowService loanPayment,
        ILoanScheduleRepository loanScheduleRepo,
        ISnackbarService? snackbar = null)
    {
        _loanMutation = loanMutation;
        _loanPayment = loanPayment;
        _loanScheduleRepo = loanScheduleRepo;
        _snackbar = snackbar;
    }

    // ── Paste loan dialog [ObservableProperty] and [RelayCommand] members ──
    // ── On success: LoanChanged?.Invoke(this, EventArgs.Empty); ──
}
```

- [ ] **Step 4: Wire both into PortfolioViewModel, update XAML, register in DI**

After moving all code out of `PortfolioViewModel.Assets.cs`, delete that file.

Use grep to find remaining XAML bindings that need path prefixes:

```bash
grep -n "Binding.*Account\|Binding.*Loan\|Binding.*Edit" Assetra.WPF/Features/Portfolio/PortfolioView.xaml
```

- [ ] **Step 5: Build, test manually (add account, edit account, add loan, pay loan), run unit tests**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: extract AccountDialogViewModel and LoanDialogViewModel from PortfolioViewModel"
```

---

### Task 10: Finalize PortfolioViewModel, update PortfolioDependencies, update tests

After all Sub-VMs are extracted, the main `PortfolioViewModel` should hold only:
- `IPortfolioLoadService`
- `IPortfolioSummaryService`
- `IPortfolioHistoryMaintenanceService`
- `IPositionDeletionWorkflowService`
- `IPositionMetadataWorkflowService`
- The 5 Sub-VM references

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioDependencies.cs`
- Modify: `Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `Assetra.Tests/WPF/PortfolioViewModelTests.cs`

- [ ] **Step 1: Slim down PortfolioServices record**

After all extractions, `PortfolioServices` should only contain the 5 remaining services plus the Sub-VM references. Remove all services that moved to Sub-VMs:

```csharp
public sealed record PortfolioServices(
    IStockService Stock,
    IPortfolioHistoryMaintenanceService? HistoryMaintenance = null,
    IPortfolioHistoryQueryService? HistoryQuery = null,
    IPortfolioLoadService? Load = null,
    IPositionDeletionWorkflowService? PositionDeletionWorkflow = null,
    IPositionMetadataWorkflowService? PositionMetadataWorkflow = null,
    IPortfolioSummaryService? Summary = null,
    AddAssetDialogViewModel? AddAssetDialog = null,
    SellPanelViewModel? SellPanel = null,
    TransactionDialogViewModel? TransactionDialog = null,
    AccountDialogViewModel? AccountDialog = null,
    LoanDialogViewModel? LoanDialog = null);
```

Also remove fields no longer needed from `PortfolioRepositories` if any repos moved to Sub-VMs.

- [ ] **Step 2: Update DI in ServiceCollectionExtensions.cs**

Rebuild the `PortfolioViewModel` constructor call with only the remaining params. Add Sub-VM registrations and pass them into `PortfolioServices`.

- [ ] **Step 3: Update PortfolioViewModelTests.cs**

The `CreateVm` helper currently sets up mocks for all services in `PortfolioServices`. Remove mocks for services that moved to Sub-VMs. Add Sub-VM construction with their own minimal mocks.

For each Sub-VM used in tests:

```csharp
var addAssetDialog = new AddAssetDialogViewModel(
    addAssetWorkflow.Object,
    snackbar: null);
```

Pass into `PortfolioServices`:

```csharp
var svc = new PortfolioServices(
    Stock: stock.Object,
    Load: loadSvc.Object,
    Summary: summary.Object,
    AddAssetDialog: addAssetDialog,
    ...);
```

Existing tests that test add-asset, sell, or transaction flows should still pass because the Sub-VMs are constructed with the same mocks.

- [ ] **Step 4: Build and run all tests**

```bash
dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: 0 errors, all tests pass.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "refactor: finalize PortfolioViewModel sub-VM split, slim PortfolioServices record"
```
