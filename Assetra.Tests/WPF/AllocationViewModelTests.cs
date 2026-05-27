using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
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
    public void Constructor_EmptyFeed_IgnoresCash()
    {
        var feed = new StubFeed { TotalCash = 1000m };
        var vm = new AllocationViewModel(feed);

        // Allocation analysis is investment-only; global cash is owned by
        // Financial Overview / Dashboard, not the investment asset page.
        Assert.Empty(vm.AllocationRows);
        Assert.Equal(0m, vm.TotalValue);
        Assert.Equal(0m, vm.TotalInvestment);
        Assert.Equal(0m, vm.TotalCash);
    }

    [Fact]
    public void Constructor_WithCash_DoesNotEmitCashRow()
    {
        var feed = new StubFeed { TotalCash = 1000m };
        feed.PositionsList.Add(Position("A", 500m));
        var vm = new AllocationViewModel(feed);

        Assert.DoesNotContain(vm.AllocationRows, r => r.Symbol == "現金");
        Assert.Equal(500m, vm.TotalValue);
        Assert.Equal(500m, vm.TotalInvestment);
        Assert.Equal(0m, vm.TotalCash);
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
    public void Constructor_WithTinyHolding_DoesNotRoundAllocationToZero()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("CORE", 10_000_000m));
        feed.PositionsList.Add(Position("DRAM", 3_000m));

        var vm = new AllocationViewModel(feed);

        var tiny = Assert.Single(vm.AllocationRows, r => r.Symbol == "DRAM");
        Assert.True(tiny.ActualPercent > 0m);
        Assert.Equal("<0.1%", tiny.ActualPercentDisplay);
        Assert.Equal(3d, tiny.AllocationBarMinWidth);
    }

    [Fact]
    public void OverviewInsightCards_SurfaceAllocationQuestionsInsteadOfDuplicatingRows()
    {
        var feed = new StubFeed();
        feed.PositionsList.Add(Position("CORE", 7_000m));
        feed.PositionsList.Add(Position("MID", 2_000m));
        feed.PositionsList.Add(Position("SMALL", 900m));
        feed.PositionsList.Add(Position("DRAM", 100m));

        var vm = new AllocationViewModel(feed);

        var insightsProperty = typeof(AllocationViewModel).GetProperty("AllocationInsightCards");
        Assert.NotNull(insightsProperty);

        var cards = Assert.IsAssignableFrom<IEnumerable<object>>(insightsProperty.GetValue(vm)).ToList();
        Assert.Equal(4, cards.Count);

        Assert.Equal("CORE", GetStringProperty(cards[0], "Primary"));
        Assert.Equal("70.0%", GetStringProperty(cards[0], "Metric"));
        Assert.Equal("前 3 大", GetStringProperty(cards[1], "Primary"));
        Assert.Equal("99.0%", GetStringProperty(cards[1], "Metric"));
        Assert.Equal("≤ 1% 持倉", GetStringProperty(cards[2], "Primary"));
        Assert.Equal("1", GetStringProperty(cards[2], "Metric"));
        Assert.Equal("持倉數", GetStringProperty(cards[3], "Primary"));
        Assert.Equal("4", GetStringProperty(cards[3], "Metric"));
    }

    [Fact]
    public void TotalCashChanged_DoesNotAffectInvestmentAllocation()
    {
        var feed = new StubFeed { TotalCash = 0m };
        feed.PositionsList.Add(Position("A", 500m));
        var vm = new AllocationViewModel(feed);
        Assert.Equal(500m, vm.TotalValue);

        feed.TotalCash = 1500m;

        Assert.Equal(500m, vm.TotalValue);
        Assert.Equal(0m, vm.TotalCash);
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

    private static string GetStringProperty(object row, string name)
    {
        var property = row.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return Assert.IsType<string>(property.GetValue(row));
    }
}
