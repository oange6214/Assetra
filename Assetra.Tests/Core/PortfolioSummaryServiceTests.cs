using Assetra.Core.DomainServices;
using Assetra.Core.Dtos;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public sealed class PortfolioSummaryServiceTests
{
    private readonly PortfolioSummaryService _sut = new();

    private static PortfolioSummaryInput EmptyInput(decimal monthlyExpense = 0m) =>
        new([], [], [], monthlyExpense);

    [Fact]
    public void Calculate_ThrowsArgumentNullException_WhenInputIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Calculate(null!));
    }

    [Fact]
    public void Calculate_ReturnsZeroTotals_WhenInputIsEmpty()
    {
        var result = _sut.Calculate(EmptyInput());

        Assert.Equal(0m, result.TotalCost);
        Assert.Equal(0m, result.TotalMarketValue);
        Assert.Equal(0m, result.TotalPnl);
        Assert.Equal(0m, result.TotalPnlPercent);
        Assert.True(result.IsTotalPositive);
        Assert.Equal(0m, result.TotalCash);
        Assert.Equal(0m, result.TotalLiabilities);
        Assert.Equal(0m, result.TotalAssets);
        Assert.Equal(0m, result.NetWorth);
        Assert.False(result.HasDayPnl);
        Assert.Empty(result.AllocationSlices);
        Assert.Empty(result.PositionWeights);
    }

    [Fact]
    public void Calculate_TotalMarketValue_SumsPositionMarketValues()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(id1, AssetType.Stock, 100m, 5000m, 6000m, 6000m, 60m, 55m, false),
            new(id2, AssetType.Fund,  50m,  2000m, 2500m, 2500m, 50m, 48m, false),
        };
        var input = new PortfolioSummaryInput(positions, [], [], 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(8500m, result.TotalMarketValue);
        Assert.Equal(7000m, result.TotalCost);
        Assert.Equal(1500m, result.TotalPnl);
    }

    [Fact]
    public void Calculate_TotalPnlPercent_IsCorrect()
    {
        var id = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(id, AssetType.Stock, 10m, 1000m, 1200m, 1200m, 120m, 100m, false),
        };
        var input = new PortfolioSummaryInput(positions, [], [], 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(20m, result.TotalPnlPercent);   // (1200-1000)/1000 * 100
    }

    [Fact]
    public void Calculate_NetWorth_EqualsAssetsMinusLiabilities()
    {
        var posId = Guid.NewGuid();
        var cashId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(posId, AssetType.Stock, 1m, 1000m, 1000m, 1000m, 1000m, 0m, true),
        };
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 5000m) };
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 3000m, 10000m) };
        var input = new PortfolioSummaryInput(positions, cashAccounts, liabilities, 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(6000m, result.TotalAssets);        // 1000 (market) + 5000 (cash)
        Assert.Equal(3000m, result.TotalLiabilities);
        Assert.Equal(3000m, result.NetWorth);           // 6000 - 3000
    }

    [Fact]
    public void Calculate_DebtRatioValue_IsCappedAt100()
    {
        var cashId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        // Liabilities exceed assets
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 1000m) };
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 5000m, 10000m) };
        var input = new PortfolioSummaryInput([], cashAccounts, liabilities, 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(100m, result.DebtRatioValue);
    }

    [Fact]
    public void Calculate_DebtRatioValue_IsCorrect_WhenBelowCap()
    {
        var cashId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 10000m) };
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 2000m, 5000m) };
        var input = new PortfolioSummaryInput([], cashAccounts, liabilities, 0m);

        var result = _sut.Calculate(input);

        // totalAssets = 10000, totalLiabilities = 2000 → 2000/10000 * 100 = 20
        Assert.Equal(20m, result.DebtRatioValue);
    }

    [Fact]
    public void Calculate_EmergencyFundMonths_DividesTotalCashByMonthlyExpense()
    {
        var cashId = Guid.NewGuid();
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 30000m) };
        var input = new PortfolioSummaryInput([], cashAccounts, [], 10000m);

        var result = _sut.Calculate(input);

        Assert.Equal(3m, result.EmergencyFundMonths);
    }

    [Fact]
    public void Calculate_EmergencyFundMonths_IsZero_WhenMonthlyExpenseIsZero()
    {
        var cashId = Guid.NewGuid();
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 50000m) };
        var input = new PortfolioSummaryInput([], cashAccounts, [], 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(0m, result.EmergencyFundMonths);
    }

    [Fact]
    public void Calculate_HasDayPnl_IsFalse_WhenAllPositionsLoadingPrice()
    {
        var id = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(id, AssetType.Stock, 100m, 5000m, 5000m, 5000m, 0m, 0m, IsLoadingPrice: true),
        };
        var input = new PortfolioSummaryInput(positions, [], [], 0m);

        var result = _sut.Calculate(input);

        Assert.False(result.HasDayPnl);
    }

    [Fact]
    public void Calculate_DayPnl_IsComputedFromPricedPositions()
    {
        var id = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            // PrevClose=100, CurrentPrice=105, Quantity=10 → dayPnl = (105-100)*10 = 50
            new(id, AssetType.Stock, 10m, 1000m, 1050m, 1050m, 105m, 100m, false),
        };
        var input = new PortfolioSummaryInput(positions, [], [], 0m);

        var result = _sut.Calculate(input);

        Assert.True(result.HasDayPnl);
        Assert.Equal(50m, result.DayPnl);
        Assert.True(result.IsDayPnlPositive);
        // dayPnlPercent = 50 / (100*10) * 100 = 5%
        Assert.Equal(5m, result.DayPnlPercent);
    }

    [Fact]
    public void Calculate_PaidPercentValue_IsClamped_WhenLiabilitiesExceedOriginal()
    {
        var loanId = Guid.NewGuid();
        // Balance > OriginalAmount (edge case — treated as 0% paid)
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 12000m, 10000m) };
        var input = new PortfolioSummaryInput([], [], liabilities, 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(0m, result.PaidPercentValue);
    }

    [Fact]
    public void Calculate_PaidPercentValue_IsCorrect()
    {
        var loanId = Guid.NewGuid();
        // Original=10000, Balance=4000 → paid = (10000-4000)/10000 * 100 = 60%
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 4000m, 10000m) };
        var input = new PortfolioSummaryInput([], [], liabilities, 0m);

        var result = _sut.Calculate(input);

        Assert.Equal(60m, result.PaidPercentValue);
    }

    [Fact]
    public void Calculate_AllocationSlices_IncludeCashAndLiabilities()
    {
        var posId = Guid.NewGuid();
        var cashId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(posId, AssetType.Stock, 1m, 1000m, 1000m, 1000m, 1000m, 0m, true),
        };
        var cashAccounts = new List<CashBalanceInput> { new(cashId, 1000m) };
        var liabilities = new List<LiabilityBalanceInput> { new(loanId, 1000m, 2000m) };
        var input = new PortfolioSummaryInput(positions, cashAccounts, liabilities, 0m);

        var result = _sut.Calculate(input);

        // allocationTotal = 1000 (stock) + 1000 (cash) + 1000 (liabilities) = 3000
        Assert.Equal(3, result.AllocationSlices.Count);
        var cashSlice = result.AllocationSlices.Single(s => s.Kind == AllocationSliceKind.Cash);
        Assert.Equal(1000m, cashSlice.Value);
        var liabSlice = result.AllocationSlices.Single(s => s.Kind == AllocationSliceKind.Liabilities);
        Assert.Equal(1000m, liabSlice.Value);
    }

    [Fact]
    public void Calculate_PositionWeights_SumTo100_WhenNetValueIsPositive()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var positions = new List<PositionSummaryInput>
        {
            new(id1, AssetType.Stock, 1m, 1000m, 1000m, 3000m, 1000m, 0m, true),
            new(id2, AssetType.Fund,  1m, 2000m, 2000m, 1000m, 2000m, 0m, true),
        };
        var input = new PortfolioSummaryInput(positions, [], [], 0m);

        var result = _sut.Calculate(input);

        var totalWeight = result.PositionWeights.Sum(w => w.Percent);
        Assert.Equal(100m, totalWeight);
    }
}
