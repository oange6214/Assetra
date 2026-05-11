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
            new PortfolioDailySnapshot(new DateOnly(2026, 3, 1), 0m, 900m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 3, 15), 0m, 1_000m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 4, 15), 0m, 1_100m, 0m, 1),
        };
        var vm = new ReturnCalendarViewModel();
        vm.UpdateSnapshots(snapshots);
        Assert.Equal(4, vm.CurrentMonth.Month);

        vm.GoPrevCommand.Execute(null);

        Assert.Equal(3, vm.CurrentMonth.Month);
        Assert.True(vm.HasData);  // March 3/15 has delta vs 3/1
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
}
