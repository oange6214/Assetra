using System.Collections.ObjectModel;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Unit tests for the L6-refactored <see cref="DividendCalendarViewModel"/>.
/// The 12-cell collection and YearTotal are now derived from the trade list
/// and rebuilt automatically; the panel is a pure data-bound view.
/// </summary>
public sealed class DividendCalendarViewModelTests
{
    private static TradeRowViewModel CashDividend(int year, int month, decimal amount) =>
        new(new Trade(
            Id: Guid.NewGuid(),
            Symbol: "0050",
            Exchange: "TWSE",
            Name: "ETF",
            Type: TradeType.CashDividend,
            TradeDate: new DateTime(year, month, 15),
            Price: 0m,
            Quantity: 0,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: amount));

    private static TradeRowViewModel Buy(int year, int month) =>
        new(new Trade(
            Id: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Buy,
            TradeDate: new DateTime(year, month, 10),
            Price: 100m,
            Quantity: 1000,
            RealizedPnl: null,
            RealizedPnlPct: null));

    [Fact]
    public void Constructor_PopulatesTwelveCells()
    {
        var trades = new ObservableCollection<TradeRowViewModel>();
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.Equal(12, vm.Cells.Count);
        Assert.All(vm.Cells, c => Assert.False(c.HasData));
    }

    [Fact]
    public void Cells_ReflectDividendsForCurrentYear()
    {
        var trades = new ObservableCollection<TradeRowViewModel>
        {
            CashDividend(2026, 3, 1500m),
            CashDividend(2026, 3,  500m),
            CashDividend(2026, 8, 9999m),
            CashDividend(2025, 3,  100m),  // different year — ignored
        };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.Equal(2000m, vm.Cells.Single(c => c.Month == 3).Total);
        Assert.Equal(9999m, vm.Cells.Single(c => c.Month == 8).Total);
        Assert.Equal(0m,    vm.Cells.Single(c => c.Month == 1).Total);
    }

    [Fact]
    public void Year_ChangedReplacesCells()
    {
        var trades = new ObservableCollection<TradeRowViewModel>
        {
            CashDividend(2025, 5, 100m),
            CashDividend(2026, 5, 200m),
        };
        var vm = new DividendCalendarViewModel(trades) { Year = 2025 };
        Assert.Equal(100m, vm.Cells.Single(c => c.Month == 5).Total);

        vm.Year = 2026;
        Assert.Equal(200m, vm.Cells.Single(c => c.Month == 5).Total);
    }

    [Fact]
    public void YearTotal_SumOfCurrentYear()
    {
        var trades = new ObservableCollection<TradeRowViewModel>
        {
            CashDividend(2026, 3, 1500m),
            CashDividend(2026, 8, 2500m),
        };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.Equal(4000m, vm.YearTotal);
        Assert.Equal("合計 4,000", vm.YearTotalDisplay);
    }

    [Fact]
    public void YearTotal_NoDividends_DisplayEmpty()
    {
        var trades = new ObservableCollection<TradeRowViewModel> { Buy(2026, 4) };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.Equal(0m, vm.YearTotal);
        Assert.Equal(string.Empty, vm.YearTotalDisplay);
    }

    [Fact]
    public void HasAnyDividends_TrueIfAnyCashDividendEverRecorded()
    {
        var trades = new ObservableCollection<TradeRowViewModel>
        {
            Buy(2026, 4),
            CashDividend(2020, 1, 50m),  // any past year still counts
        };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.True(vm.HasAnyDividends);
    }

    [Fact]
    public void HasAnyDividends_FalseWhenOnlyNonDividendTrades()
    {
        var trades = new ObservableCollection<TradeRowViewModel> { Buy(2026, 4) };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };

        Assert.False(vm.HasAnyDividends);
    }

    [Fact]
    public void TradesCollectionChanged_TriggersRebuild()
    {
        var trades = new ObservableCollection<TradeRowViewModel>();
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };
        Assert.Equal(0m, vm.Cells.Single(c => c.Month == 6).Total);

        trades.Add(CashDividend(2026, 6, 777m));

        Assert.Equal(777m, vm.Cells.Single(c => c.Month == 6).Total);
        Assert.True(vm.HasAnyDividends);
    }

    [Fact]
    public void PrevYear_NextYear_AdjustsYearAndCells()
    {
        var trades = new ObservableCollection<TradeRowViewModel>
        {
            CashDividend(2025, 1, 100m),
            CashDividend(2027, 1, 300m),
        };
        var vm = new DividendCalendarViewModel(trades) { Year = 2026 };
        Assert.Equal(0m, vm.Cells[0].Total);

        vm.PrevYearCommand.Execute(null);
        Assert.Equal(2025, vm.Year);
        Assert.Equal(100m, vm.Cells[0].Total);

        vm.NextYearCommand.Execute(null);
        vm.NextYearCommand.Execute(null);
        Assert.Equal(2027, vm.Year);
        Assert.Equal(300m, vm.Cells[0].Total);
    }
}
