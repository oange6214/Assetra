using Assetra.Core.Models;
using Assetra.WPF.Features.FinancialOverview.Calendar;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class ReturnCalendarViewModelTests
{
    [Fact]
    public void Empty_HasNoDataAndCannotGoPrev()
    {
        var vm = new ReturnCalendarViewModel();

        Assert.False(vm.HasData);
        Assert.False(vm.CanGoPrev);
        Assert.Equal(42, vm.Cells.Count); // 6 weeks × 7 days grid always renders
    }

    [Fact]
    public void UpdateSnapshots_JumpsToLatestMonthWithData()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 2), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdateSnapshots(snapshots);

        Assert.Equal(new DateOnly(2026, 4, 1), vm.CurrentMonth);
        Assert.True(vm.HasData);
    }

    [Fact]
    public void UpdatePortfolioData_AdjustsBuyCashFlowOutOfSnapshotDelta()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 14), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 15), 0m, 5_000m, 0m, 1),
        };
        var trades = new[]
        {
            MakeTrade(new DateOnly(2026, 5, 15), TradeType.Buy, price: 100m, quantity: 40),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdatePortfolioData(snapshots, trades);

        var day15 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 15));
        Assert.True(day15.HasData);
        Assert.Equal(0m, day15.Delta);
        Assert.Equal(0m, vm.MonthlyAbsolutePnl);
    }

    [Fact]
    public void UpdatePortfolioData_KeepsMarketSnapshotReturnWhenNoCashFlow()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 14), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 15), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdatePortfolioData(snapshots, []);

        var day15 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 15));
        Assert.True(day15.HasData);
        Assert.Equal(100m, day15.Delta);
        Assert.Equal(100m, vm.MonthlyAbsolutePnl);
    }

    [Fact]
    public void UpdateSnapshots_IgnoresWeekendSnapshots()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 14), 0m, 900m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 15), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 16), 0m, 2_000m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdateSnapshots(snapshots);

        var day15 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 15));
        var day16 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 16));
        Assert.True(day15.HasData);
        Assert.Equal(100m, day15.Delta);
        Assert.False(day16.HasData);
        Assert.Null(day16.Delta);
        Assert.Equal(100m, vm.MonthlyAbsolutePnl);
    }

    [Fact]
    public void UpdateSnapshots_PrefersWeekendBreakdownSnapshotForPreviousTradingDate()
    {
        var snapshots = new[]
        {
            MakeSnapshot(new DateOnly(2026, 5, 14), 1_000m, withBreakdown: true),
            MakeSnapshot(new DateOnly(2026, 5, 15), 100m),
            MakeSnapshot(new DateOnly(2026, 5, 16), 1_100m, withBreakdown: true),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdateSnapshots(snapshots);

        var day15 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 15));
        var day16 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 16));
        Assert.True(day15.HasData);
        Assert.Equal(100m, day15.Delta);
        Assert.False(day16.HasData);
        Assert.Null(day16.Delta);
        Assert.Equal(100m, vm.MonthlyAbsolutePnl);
    }

    [Fact]
    public void UpdateSnapshots_IgnoresLegacySnapshotAfterBreakdownSnapshotsBegin()
    {
        var snapshots = new[]
        {
            MakeSnapshot(new DateOnly(2026, 5, 14), 1_000m, withBreakdown: true),
            MakeSnapshot(new DateOnly(2026, 5, 15), 100m),
            MakeSnapshot(new DateOnly(2026, 5, 18), 1_100m, withBreakdown: true),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdateSnapshots(snapshots);

        var day15 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 15));
        var day18 = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 18));
        Assert.False(day15.HasData);
        Assert.Null(day15.Delta);
        Assert.True(day18.HasData);
        Assert.Equal(100m, day18.Delta);
        Assert.Equal(100m, vm.MonthlyAbsolutePnl);
    }

    [Fact]
    public void Cells_PopulateDailyDeltaAndTone()
    {
        // Day 1: 1000, Day 2: 1100 (+10%, strongest up), Day 3: 1090 (-0.9%, weak down)
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 2), 0m, 1_100m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 3), 0m, 1_090m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();

        vm.UpdateSnapshots(snapshots);

        // Day 2 (+10%) has Delta=100, tone = UpStrongest
        var day2 = vm.Cells.First(c => c.Date == new DateOnly(2026, 4, 2));
        Assert.True(day2.HasData);
        Assert.Equal(100m, day2.Delta);
        Assert.Equal(CellTone.UpStrongest, day2.Tone);
        // Day 3 (-0.91%) has Delta=-10; |pct|=0.91% → bucket 2 (0.5–1.5%) → DownMedium
        var day3 = vm.Cells.First(c => c.Date == new DateOnly(2026, 4, 3));
        Assert.Equal(-10m, day3.Delta);
        Assert.Equal(CellTone.DownMedium, day3.Tone);
    }

    [Fact]
    public void GoPrev_AdjustsMonthAndRebuilds()
    {
        // 兩個月各 2 個 snapshot，確保每月內都有 delta（需要前一筆 baseline）。
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 3, 2), 0m, 900m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 3, 16), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 15), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);
        Assert.Equal(4, vm.CurrentMonth.Month);

        vm.GoPrevCommand.Execute(null);

        Assert.Equal(3, vm.CurrentMonth.Month);
        Assert.True(vm.HasData);  // March 3/15 has delta vs 3/1
    }

    // ── New tests for weekly column + month dropdown + cell popover (Plan tasks 5/6/7) ──

    [Fact]
    public void Weeks_BuildSixRowsEachWithSevenDays()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_000m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        Assert.Equal(6, vm.Weeks.Count);
        Assert.All(vm.Weeks, w => Assert.Equal(7, w.Days.Count));
    }

    [Fact]
    public void Weeks_WeekTotalSumsCurrentMonthDeltas()
    {
        // 5/4 = +200; 5/5 = -100 → 該週 (5/4–5/10 = Mon-Sun) total = +100
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_200m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 5), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        // 找到包含 5/4 的那列
        var weekWith0504 = vm.Weeks.First(w => w.Days.Any(d => d.Date == new DateOnly(2026, 5, 4)));
        Assert.Equal(100m, weekWith0504.TotalDelta);
        Assert.NotEqual("—", weekWith0504.TotalDisplay);
    }

    [Fact]
    public void AvailableMonths_ListsFromEarliestToLatestDescending()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 2, 2), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 1), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        Assert.NotEmpty(vm.AvailableMonths);
        // 倒序：第一個應該 >= 第二個（AvailableMonths 是 MonthOption，比 .Value）
        Assert.True(vm.AvailableMonths[0].Value >= vm.AvailableMonths[^1].Value);
        // 應該包含至少 2/1 到 4/1 之間的月份
        var values = vm.AvailableMonths.Select(m => m.Value).ToList();
        Assert.Contains(new DateOnly(2026, 2, 1), values);
        Assert.Contains(new DateOnly(2026, 4, 1), values);
    }

    [Fact]
    public void SelectCell_OpensPopoverForDataCell()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        var cell = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 4));
        Assert.False(vm.IsCellPopoverOpen);

        vm.SelectCellCommand.Execute(cell);

        Assert.True(vm.IsCellPopoverOpen);
        Assert.Same(cell, vm.SelectedCell);
    }

    [Fact]
    public void SelectCell_IgnoresEmptyCell()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        // 找一個 IsCurrentMonth=true 但 HasData=false 的 cell
        var emptyCell = vm.Cells.FirstOrDefault(c => c.IsCurrentMonth && !c.HasData);
        Assert.NotNull(emptyCell);

        vm.SelectCellCommand.Execute(emptyCell);

        Assert.False(vm.IsCellPopoverOpen);
    }

    [Fact]
    public void CloseCellPopover_ClearsSelectedCell()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);
        var cell = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 4));
        vm.SelectCellCommand.Execute(cell);
        Assert.True(vm.IsCellPopoverOpen);

        vm.CloseCellPopoverCommand.Execute(null);

        Assert.False(vm.IsCellPopoverOpen);
        Assert.Null(vm.SelectedCell);
    }

    // ── v2 tests ──

    [Fact]
    public void UseAbsoluteForTone_RecomputesCellTone()
    {
        // 5/4 Δ=+50 → 5% (UpStrongest by pct); 但絕對值 50 < 1000 → UpWeak by abs
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_050m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);
        var cell = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 4));
        Assert.Equal(CellTone.UpStrongest, cell.Tone);

        vm.UseAbsoluteForTone = true;
        cell = vm.Cells.First(c => c.Date == new DateOnly(2026, 5, 4));
        Assert.Equal(CellTone.UpWeak, cell.Tone);   // |50| < 1000 → weak bucket
    }

    [Fact]
    public void IsYearView_ProducesYearCells_365Or366()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        var yearCells = vm.YearViewCells;
        // 2026 is non-leap year → 365 days
        Assert.Equal(365, yearCells.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), yearCells[0].Date);
        Assert.Equal(new DateOnly(2026, 12, 31), yearCells[^1].Date);
    }

    [Fact]
    public void YearViewCells_ColorsDaysWithSnapshotDelta()
    {
        // 跨月場景：5/1 baseline, 5/4 +200 (20%) → 應有 tone
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 1), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 5, 4), 0m, 1_200m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        var dayWithData = vm.YearViewCells.First(c => c.Date == new DateOnly(2026, 5, 4));
        Assert.True(dayWithData.HasData);
        Assert.NotEqual(CellTone.None, dayWithData.Tone);

        // 1/1（年初無前一日 baseline）應該 HasData=false → Tone=None
        var jan1 = vm.YearViewCells.First(c => c.Date == new DateOnly(2026, 1, 1));
        Assert.Equal(CellTone.None, jan1.Tone);
    }

    [Fact]
    public void MonthlyKpis_SumAndComputePercent()
    {
        // 4/1: 1000, 4/30: 1200 → +200, +20%
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 3, 31), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 1), 0m, 1_100m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 30), 0m, 1_200m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);

        // 4/1 Δ = 100 (relative to 3/31 = 1000); 4/30 Δ = 100 (relative to 4/1 = 1100).
        // 月內 delta 累加 = 200
        Assert.Equal(200m, vm.MonthlyAbsolutePnl);
        // monthOpenValue = 1000 (last snapshot before earliest-in-month 4/1),
        // lastValue = 1200 → (1200-1000)/1000 = 0.20
        Assert.Equal(0.20m, vm.MonthlyReturnPct);
    }

    private static PortfolioDailySnapshot MakeSnapshot(DateOnly date, decimal marketValue, bool withBreakdown = false) =>
        withBreakdown
            ? new PortfolioDailySnapshot(
                date,
                TotalCost: 0m,
                MarketValue: marketValue,
                Pnl: 0m,
                PositionCount: 1,
                Currency: "TWD",
                CashValue: 0m,
                EquityValue: marketValue,
                LiabilityValue: 0m)
            : new PortfolioDailySnapshot(date, 0m, marketValue, 0m, 1);

    private static Trade MakeTrade(
        DateOnly date,
        TradeType type,
        decimal? realizedPnl = null,
        decimal? cashAmount = null,
        decimal price = 0m,
        int quantity = 1,
        decimal? commission = null,
        Guid? parentTradeId = null) =>
        new(
            Guid.NewGuid(),
            "2330",
            "TWSE",
            "台積電",
            type,
            date.ToDateTime(TimeOnly.MinValue),
            price,
            quantity,
            realizedPnl,
            null,
            cashAmount,
            Commission: commission,
            ParentTradeId: parentTradeId);
}
