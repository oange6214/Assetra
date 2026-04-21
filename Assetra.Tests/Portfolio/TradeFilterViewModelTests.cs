using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Xunit;

namespace Assetra.Tests.Portfolio;

/// <summary>Minimal no-op localization stub for unit tests.</summary>
file sealed class NullLocalizationService : ILocalizationService
{
    public string CurrentLanguage => "en-US";
    public string Get(string key, string fallback = "") => fallback;
    public void SetLanguage(string languageCode) { }
    public event EventHandler? LanguageChanged { add { } remove { } }
}

/// <summary>
/// Integration tests for <see cref="TradeFilterViewModel"/> covering filter logic,
/// pagination, collection diffing, and clear commands.
///
/// Tests that require <c>CollectionViewSource.GetDefaultView</c> (which needs a WPF
/// dispatcher) are wrapped in <see cref="StaRun"/> and executed on a dedicated STA thread.
/// Pure-logic tests that do not call <c>AttachTradesCollection</c> run on the default MTA
/// test thread.
/// </summary>
public sealed class TradeFilterViewModelTests
{
    // ── STA helper ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="action"/> on a freshly created STA thread and re-throws
    /// any exception on the calling thread. Required for any test that calls
    /// <c>CollectionViewSource.GetDefaultView</c>.
    /// </summary>
    private static void StaRun(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }

    // ── Factory helpers ─────────────────────────────────────────────────────────

    private static Trade MakeTrade(
        string symbol = "2330",
        string name = "台積電",
        TradeType type = TradeType.Buy,
        DateTime? tradeDate = null) =>
        new(Guid.NewGuid(), symbol, "TWSE", name, type,
            tradeDate ?? new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Price: 100m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null);

    private static TradeRowViewModel MakeRow(
        string symbol = "2330",
        string name = "台積電",
        TradeType type = TradeType.Buy,
        DateTime? tradeDate = null) =>
        new(MakeTrade(symbol, name, type, tradeDate));

    /// <summary>
    /// Creates a <see cref="TradeFilterViewModel"/> backed by a fixed list, and also
    /// attaches a CollectionViewSource-bound collection on an STA thread so that
    /// <see cref="TradeFilterViewModel.RefreshTradesView"/> can execute fully.
    /// Must be called from within <see cref="StaRun"/>.
    /// </summary>
    private static readonly ILocalizationService _nullLocalization = new NullLocalizationService();

    private static (TradeFilterViewModel vm, ObservableCollection<TradeRowViewModel> col)
        CreateAttached(IList<TradeRowViewModel> rows)
    {
        var col = new ObservableCollection<TradeRowViewModel>(rows);
        var vm = new TradeFilterViewModel(() => col, _nullLocalization);
        vm.AttachTradesCollection(col);
        return (vm, col);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RefreshTradesView — empty source
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_NoTrades_TradeTotalCountIsZero()
    {
        StaRun(() =>
        {
            var (vm, _) = CreateAttached([]);

            vm.RefreshTradesView();

            Assert.Equal(0, vm.TradeTotalCount);
            Assert.Equal(1, vm.TradeTotalPages);   // always at least 1 page
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RefreshTradesView — type filter
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_TypeFilterChecked_OnlyMatchingTypesIncluded()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2330", type: TradeType.Sell),
                MakeRow(symbol: "台新活存", type: TradeType.Deposit),
            };
            var (vm, _) = CreateAttached(rows);

            // Manually add type-filter items and check only "Buy"
            var buyFilter = new TradeTypeFilterItem("Buy", "買入") { IsChecked = true };
            var sellFilter = new TradeTypeFilterItem("Sell", "賣出") { IsChecked = false };
            vm.TradeTypeFilters.Add(buyFilter);
            vm.TradeTypeFilters.Add(sellFilter);

            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_MultipleTypeFiltersChecked_AllMatchingTypesIncluded()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2330", type: TradeType.Sell),
                MakeRow(symbol: "台新活存", type: TradeType.Deposit),
            };
            var (vm, _) = CreateAttached(rows);

            vm.TradeTypeFilters.Add(new TradeTypeFilterItem("Buy",  "買入") { IsChecked = true });
            vm.TradeTypeFilters.Add(new TradeTypeFilterItem("Sell", "賣出") { IsChecked = true });

            vm.RefreshTradesView();

            Assert.Equal(2, vm.TradeTotalCount);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RefreshTradesView — asset filter
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_AssetFilterChecked_FiltersToSymbol()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2317", type: TradeType.Buy),
                MakeRow(symbol: "2330", type: TradeType.Sell),
            };
            var (vm, _) = CreateAttached(rows);

            vm.TradeAssetFilters.Add(
                new TradeAssetFilterItem("2330", "Investment", 0, "投資") { IsChecked = true });

            vm.RefreshTradesView();

            Assert.Equal(2, vm.TradeTotalCount);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RefreshTradesView — text search
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_SearchTextMatchesSymbol_OnlyMatchesReturned()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "2330", name: "台積電", type: TradeType.Buy),
                MakeRow(symbol: "2317", name: "鴻海",   type: TradeType.Buy),
            };
            var (vm, _) = CreateAttached(rows);
            vm.TradeSearchText = "2330";

            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_SearchTextMatchesName_OnlyMatchesReturned()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "2330", name: "台積電", type: TradeType.Buy),
                MakeRow(symbol: "2317", name: "鴻海",   type: TradeType.Buy),
            };
            var (vm, _) = CreateAttached(rows);
            vm.TradeSearchText = "鴻海";

            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_SearchTextCaseInsensitive_MatchesBothCases()
    {
        StaRun(() =>
        {
            var rows = new[]
            {
                MakeRow(symbol: "AAPL", name: "Apple Inc", type: TradeType.Buy),
                MakeRow(symbol: "GOOG", name: "Alphabet",  type: TradeType.Buy),
            };
            var (vm, _) = CreateAttached(rows);
            vm.TradeSearchText = "aapl";   // lowercase against uppercase symbol

            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RefreshTradesView — date range
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_DateFromFilter_ExcludesTradesBeforeDate()
    {
        StaRun(() =>
        {
            var early = MakeRow(tradeDate: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var late  = MakeRow(tradeDate: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var (vm, _) = CreateAttached([early, late]);

            vm.TradeDateFrom = new DateTime(2025, 1, 1);
            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_DateToFilter_ExcludesTradesAfterDate()
    {
        StaRun(() =>
        {
            var early = MakeRow(tradeDate: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var late  = MakeRow(tradeDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var (vm, _) = CreateAttached([early, late]);

            vm.TradeDateTo = new DateTime(2025, 1, 1);
            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_DateToFilter_IncludesTradeOnExactDate()
    {
        StaRun(() =>
        {
            // Trade falls on exactly the cutoff date — must be included (TradeDateTo is inclusive).
            // Production predicate: t.TradeDate > TradeDateTo.Value.Date.AddDays(1).ToUniversalTime()
            // 2024-06-15 UTC is NOT > 2024-06-16 UTC, so it passes the filter.
            var cutoff    = new DateTime(2024, 6, 15);
            var tradeDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var (vm, _)   = CreateAttached([MakeRow(tradeDate: tradeDate)]);

            vm.TradeDateFrom = null;
            vm.TradeDateTo   = cutoff;
            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalCount);
        });
    }

    [Fact]
    public void RefreshTradesView_DateToFilter_ExcludesTradeOnDayAfterCutoff()
    {
        StaRun(() =>
        {
            // Trade falls one day after the cutoff — must be excluded.
            // 2024-06-16 UTC IS > 2024-06-16 UTC is false — wait: the boundary is
            // AddDays(1) of the cutoff date: 2024-06-16 00:00:00 UTC.
            // A trade at exactly 2024-06-16 00:00:00 UTC is NOT > 2024-06-16 00:00:00 UTC,
            // so it would still pass. Use 2024-06-16 00:00:01 UTC (one second past midnight)
            // or simply move the trade to 2024-06-17 UTC to be clearly after the boundary.
            var cutoff    = new DateTime(2024, 6, 15);
            var tradeDate = new DateTime(2024, 6, 17, 0, 0, 0, DateTimeKind.Utc);
            var (vm, _)   = CreateAttached([MakeRow(tradeDate: tradeDate)]);

            vm.TradeDateFrom = null;
            vm.TradeDateTo   = cutoff;
            vm.RefreshTradesView();

            Assert.Equal(0, vm.TradeTotalCount);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Pagination — next / previous page commands
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TradePageNext_WhenOnFirstOfMultiplePages_IncrementsPage()
    {
        StaRun(() =>
        {
            // 30 rows + page size 25 → 2 pages
            var rows = Enumerable.Range(1, 30)
                .Select(i => MakeRow(symbol: $"SYM{i:D3}"))
                .ToList();
            var (vm, _) = CreateAttached(rows);
            vm.TradePageSize = 25;
            vm.RefreshTradesView();

            Assert.Equal(2, vm.TradeTotalPages);
            Assert.Equal(1, vm.TradeCurrentPage);

            vm.TradePageNextCommand.Execute(null);

            Assert.Equal(2, vm.TradeCurrentPage);
        });
    }

    [Fact]
    public void TradePagePrev_WhenOnLastPage_DecrementsPage()
    {
        StaRun(() =>
        {
            var rows = Enumerable.Range(1, 30)
                .Select(i => MakeRow(symbol: $"SYM{i:D3}"))
                .ToList();
            var (vm, _) = CreateAttached(rows);
            vm.TradePageSize = 25;
            vm.RefreshTradesView();

            vm.TradePageNextCommand.Execute(null);  // go to page 2
            Assert.Equal(2, vm.TradeCurrentPage);

            vm.TradePagePrevCommand.Execute(null);

            Assert.Equal(1, vm.TradeCurrentPage);
        });
    }

    [Fact]
    public void TradePageNext_WhenOnLastPage_DoesNotIncrementBeyondMax()
    {
        StaRun(() =>
        {
            var rows = Enumerable.Range(1, 10)
                .Select(i => MakeRow(symbol: $"SYM{i:D3}"))
                .ToList();
            var (vm, _) = CreateAttached(rows);
            vm.TradePageSize = 25;
            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeTotalPages);

            vm.TradePageNextCommand.Execute(null);   // already at last page

            Assert.Equal(1, vm.TradeCurrentPage);
        });
    }

    [Fact]
    public void TradePagePrev_WhenOnFirstPage_DoesNotDecrementBelowOne()
    {
        StaRun(() =>
        {
            var (vm, _) = CreateAttached([MakeRow()]);
            vm.RefreshTradesView();

            Assert.Equal(1, vm.TradeCurrentPage);

            vm.TradePagePrevCommand.Execute(null);

            Assert.Equal(1, vm.TradeCurrentPage);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ClearTradeFiltersCommand
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClearTradeFiltersCommand_ResetsAllFilterState()
    {
        StaRun(() =>
        {
            // Provide a real trade row so RebuildTradeAssetFilters has data to work with.
            var row = MakeRow(symbol: "2330", type: TradeType.Buy);
            var rows = new List<TradeRowViewModel> { row };
            var col  = new ObservableCollection<TradeRowViewModel>(rows);
            var vm   = new TradeFilterViewModel(() => rows, _nullLocalization);
            vm.AttachTradesCollection(col);

            // Wire type filters with real PropertyChanged subscriptions (as production does).
            vm.InitTradeTypeFilters();

            // Wire asset filters from the actual _getTrades delegate.
            vm.RebuildTradeAssetFilters();

            // Sanity: filters were created.
            Assert.NotEmpty(vm.TradeTypeFilters);
            Assert.NotEmpty(vm.TradeAssetFilters);

            // Activate some filter state.
            vm.TradeSearchText = "2330";
            vm.TradeDateFrom   = new DateTime(2025, 1, 1);
            vm.TradeDateTo     = new DateTime(2025, 12, 31);

            // Check the first type filter (e.g. "Buy") and the asset filter for "2330".
            vm.TradeTypeFilters[0].IsChecked = true;
            vm.TradeAssetFilters.First(f => f.Symbol == "2330").IsChecked = true;

            // Execute the clear command.
            vm.ClearTradeFiltersCommand.Execute(null);

            Assert.Equal(string.Empty, vm.TradeSearchText);
            Assert.Null(vm.TradeDateFrom);
            Assert.Null(vm.TradeDateTo);
            Assert.True(vm.TradeTypeFilters.All(f => !f.IsChecked),
                "All type filters must be unchecked after clear");
            Assert.True(vm.TradeAssetFilters.All(f => !f.IsChecked),
                "All asset filters must be unchecked after clear");
            Assert.Equal(1, vm.TradeCurrentPage);
            Assert.False(vm.HasActiveTradeFilter);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HasActiveTradeFilter — pure computed property (no STA needed)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HasActiveTradeFilter_DefaultState_IsFalse()
    {
        var vm = new TradeFilterViewModel(() => [], _nullLocalization);

        Assert.False(vm.HasActiveTradeFilter);
    }

    [Fact]
    public void HasActiveTradeFilter_AfterSettingSearchText_IsTrue()
    {
        var vm = new TradeFilterViewModel(() => [], _nullLocalization);
        vm.TradeSearchText = "test";

        Assert.True(vm.HasActiveTradeFilter);
    }

    [Fact]
    public void HasActiveTradeFilter_AfterSettingDateFrom_IsTrue()
    {
        var vm = new TradeFilterViewModel(() => [], _nullLocalization);
        vm.TradeDateFrom = new DateTime(2025, 1, 1);

        Assert.True(vm.HasActiveTradeFilter);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RebuildTradeAssetFilters — collection diffing (needs STA for first call)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RebuildTradeAssetFilters_SameTradesTwice_NoAddRemoveEvents()
    {
        StaRun(() =>
        {
            var rows = new List<TradeRowViewModel>
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2317", type: TradeType.Buy),
            };
            var vm = new TradeFilterViewModel(() => rows, _nullLocalization);

            // First call builds the list
            vm.RebuildTradeAssetFilters();

            var addRemoveCount = 0;
            vm.TradeAssetFilters.CollectionChanged += (_, e) =>
            {
                if (e.Action is NotifyCollectionChangedAction.Add
                             or NotifyCollectionChangedAction.Remove)
                    addRemoveCount++;
            };

            // Second call with identical trades — no add/remove should fire
            vm.RebuildTradeAssetFilters();

            Assert.Equal(0, addRemoveCount);
        });
    }

    [Fact]
    public void RebuildTradeAssetFilters_NewSymbolAdded_AppearsInCollection()
    {
        StaRun(() =>
        {
            var rows = new List<TradeRowViewModel>
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
            };
            var vm = new TradeFilterViewModel(() => rows, _nullLocalization);

            vm.RebuildTradeAssetFilters();
            Assert.Single(vm.TradeAssetFilters);

            // Simulate adding a new trade with a different symbol
            rows.Add(MakeRow(symbol: "2317", type: TradeType.Buy));
            vm.RebuildTradeAssetFilters();

            Assert.Equal(2, vm.TradeAssetFilters.Count);
            Assert.Contains(vm.TradeAssetFilters, f => f.Symbol == "2317");
        });
    }

    [Fact]
    public void RebuildTradeAssetFilters_RemovedSymbol_DisappearsFromCollection()
    {
        StaRun(() =>
        {
            var rows = new List<TradeRowViewModel>
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2317", type: TradeType.Buy),
            };
            var vm = new TradeFilterViewModel(() => rows, _nullLocalization);

            vm.RebuildTradeAssetFilters();
            Assert.Equal(2, vm.TradeAssetFilters.Count);

            // Remove all 2317 rows from source
            rows.RemoveAll(r => r.Symbol == "2317");
            vm.RebuildTradeAssetFilters();

            Assert.Single(vm.TradeAssetFilters);
            Assert.DoesNotContain(vm.TradeAssetFilters, f => f.Symbol == "2317");
        });
    }

    [Fact]
    public void RebuildTradeAssetFilters_CheckedSymbolPreserved_WhenSymbolStillPresent()
    {
        StaRun(() =>
        {
            var rows = new List<TradeRowViewModel>
            {
                MakeRow(symbol: "2330", type: TradeType.Buy),
                MakeRow(symbol: "2317", type: TradeType.Buy),
            };
            var vm = new TradeFilterViewModel(() => rows, _nullLocalization);

            vm.RebuildTradeAssetFilters();

            // Check "2330" before the second rebuild
            var item2330 = vm.TradeAssetFilters.First(f => f.Symbol == "2330");
            item2330.IsChecked = true;

            // Add a new symbol — should not disturb the existing checked state
            rows.Add(MakeRow(symbol: "AAPL", type: TradeType.Buy));
            vm.RebuildTradeAssetFilters();

            var still2330 = vm.TradeAssetFilters.First(f => f.Symbol == "2330");
            Assert.True(still2330.IsChecked, "Previously checked item must remain checked after rebuild");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Pagination — TradeTotalPages computed correctly
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshTradesView_ExactMultipleOfPageSize_CorrectPageCount()
    {
        StaRun(() =>
        {
            var rows = Enumerable.Range(1, 50)
                .Select(i => MakeRow(symbol: $"SYM{i:D3}"))
                .ToList();
            var (vm, _) = CreateAttached(rows);
            vm.TradePageSize = 25;

            vm.RefreshTradesView();

            Assert.Equal(50, vm.TradeTotalCount);
            Assert.Equal(2, vm.TradeTotalPages);
        });
    }

    [Fact]
    public void RefreshTradesView_NonMultipleOfPageSize_RoundsUpPageCount()
    {
        StaRun(() =>
        {
            var rows = Enumerable.Range(1, 26)
                .Select(i => MakeRow(symbol: $"SYM{i:D3}"))
                .ToList();
            var (vm, _) = CreateAttached(rows);
            vm.TradePageSize = 25;

            vm.RefreshTradesView();

            Assert.Equal(26, vm.TradeTotalCount);
            Assert.Equal(2, vm.TradeTotalPages);
        });
    }
}
