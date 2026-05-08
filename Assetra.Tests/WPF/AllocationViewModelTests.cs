using System.Collections.ObjectModel;
using System.ComponentModel;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.Contracts;
using Assetra.WPF.Features.Portfolio.Controls;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Unit tests for the L3-decoupled <see cref="AllocationViewModel"/>. Exercises
/// the rebuild logic against a hand-rolled <see cref="IPortfolioPositionFeed"/>
/// stub — no PortfolioViewModel construction required.
/// </summary>
public sealed class AllocationViewModelTests
{
    /// <summary>Minimal feed that exposes a mutable ObservableCollection so tests can drive change events.</summary>
    private sealed class StubFeed : IPortfolioPositionFeed, INotifyPropertyChanged
    {
        public ObservableCollection<PortfolioRowViewModel> PositionsList { get; } = new();
        public IReadOnlyList<PortfolioRowViewModel> Positions => PositionsList;

        private decimal _totalCash;
        public decimal TotalCash
        {
            get => _totalCash;
            set
            {
                if (_totalCash == value) return;
                _totalCash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCash)));
            }
        }

        public decimal TotalMarketValue { get; set; }
        public decimal TotalCost { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static PortfolioRowViewModel Position(string symbol, decimal marketValue, decimal pnl = 0m) =>
        new()
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Name = symbol,
            AssetType = AssetType.Stock,
            Quantity = 100m,
            CurrentPrice = marketValue / 100m,
            MarketValue = marketValue,
            Pnl = pnl,
            Currency = "TWD",
        };

    [Fact]
    public void Constructor_NullFeed_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AllocationViewModel(portfolio: null!));
    }

    [Fact]
    public void Constructor_EmptyFeed_BuildsCashOnlyRow()
    {
        var feed = new StubFeed { TotalCash = 1000m };
        var vm = new AllocationViewModel(feed);

        // Cash always gets a dedicated row when total > 0 — symbol "現金"
        var cashRow = vm.AllocationRows.FirstOrDefault(r => r.Symbol == "現金");
        Assert.NotNull(cashRow);
        Assert.Equal(1000m, cashRow!.MarketValue);
        Assert.Equal(1000m, vm.TotalValue);
        Assert.Equal(0m, vm.TotalInvestment);
    }

    [Fact]
    public void Constructor_WithPositions_AggregatesByValueDescending()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("A", 300m));
        feed.PositionsList.Add(Position("B", 700m));
        feed.PositionsList.Add(Position("C", 100m));

        var vm = new AllocationViewModel(feed);

        var investRows = vm.AllocationRows.Where(r => r.Symbol != "現金").ToList();
        Assert.Equal(["B", "A", "C"], investRows.Select(r => r.Symbol));
        Assert.Equal(1100m, vm.TotalInvestment);
        Assert.Equal(3, vm.AssetCount);
    }

    [Fact]
    public void TotalCashChanged_TriggersRebuild()
    {
        var feed = new StubFeed { TotalCash = 0m };
        feed.PositionsList.Add(Position("A", 500m));
        var vm = new AllocationViewModel(feed);
        Assert.Equal(500m, vm.TotalValue);

        feed.TotalCash = 1500m;

        Assert.Equal(2000m, vm.TotalValue);
        Assert.Equal(1500m, vm.TotalCash);
    }

    [Fact]
    public void PositionAdded_TriggersRebuildAndCountUpdate()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("A", 500m));
        var vm = new AllocationViewModel(feed);
        Assert.Equal(1, vm.AssetCount);

        feed.PositionsList.Add(Position("B", 300m));

        Assert.Equal(2, vm.AssetCount);
        Assert.Equal(800m, vm.TotalInvestment);
    }

    [Fact]
    public void PositionMarketValueChanged_TriggersRebuild()
    {
        var feed = new StubFeed();
        var a = Position("A", 500m);
        feed.PositionsList.Add(a);
        var vm = new AllocationViewModel(feed);
        Assert.Equal(500m, vm.TotalInvestment);

        a.MarketValue = 1500m;

        Assert.Equal(1500m, vm.TotalInvestment);
    }

    [Fact]
    public void Dispose_UnsubscribesFromFeed()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("A", 500m));
        var vm = new AllocationViewModel(feed);
        var initialTotal = vm.TotalInvestment;

        vm.Dispose();

        // After Dispose, further changes should not affect the VM.
        feed.TotalCash = 99999m;
        feed.PositionsList.Add(Position("B", 9999m));

        Assert.Equal(initialTotal, vm.TotalInvestment);
    }

    [Fact]
    public void TotalPnl_AggregatesAcrossPositions()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("A", 500m, pnl: 50m));
        feed.PositionsList.Add(Position("B", 300m, pnl: -20m));
        var vm = new AllocationViewModel(feed);

        Assert.Equal(30m, vm.TotalPnl);
        Assert.True(vm.IsTotalPnlPositive);
    }
}
