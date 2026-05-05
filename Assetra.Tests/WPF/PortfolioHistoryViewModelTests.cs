using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class PortfolioHistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_ConvertsSnapshotCurrenciesToBaseCurrency()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 32_000m, 0m, 1, "TWD"),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 1_000m, 0m, 1, "USD"),
        };
        var vm = new PortfolioHistoryViewModel(
            new StubHistoryQuery(snapshots),
            settings: new FakeSettings("TWD"),
            fx: new StubFx(new Dictionary<(string From, string To), decimal>
            {
                { ("USD", "TWD"), 32m },
            }));

        await vm.LoadAsync();

        var values = ChartValues(vm);
        Assert.Equal([32_000d, 32_000d], values);
    }

    [Fact]
    public async Task ChangePeriodAll_IncludesSnapshotsOlderThanTenYears()
    {
        var oldDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-12));
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(oldDate, 0m, 10m, 0m, 1),
            new PortfolioDailySnapshot(DateOnly.FromDateTime(DateTime.Today), 0m, 20m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();
        vm.ChangePeriodCommand.Execute("All");
        await Task.Yield();

        Assert.Equal("All", vm.ActivePeriod);
        Assert.Equal([10d, 20d], ChartValues(vm));
    }

    [Fact]
    public async Task LoadAsync_WhenFilteredRangeHasNoSnapshots_ShowsEmptyState()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 10m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots))
        {
            CustomStartDate = new DateTime(2030, 1, 1),
            CustomEndDate = new DateTime(2030, 1, 31),
        };

        await vm.LoadAsync();

        Assert.False(vm.HasHistory);
        Assert.Empty(vm.ValueSeries);
    }

    [Fact]
    public async Task CustomDateChange_RefreshesChartWithSelectedRange()
    {
        var snapshots = new[]
        {
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 1), 0m, 10m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 2), 0m, 20m, 0m, 1),
            new PortfolioDailySnapshot(new DateOnly(2026, 1, 3), 0m, 30m, 0m, 1),
        };
        var vm = new PortfolioHistoryViewModel(new StubHistoryQuery(snapshots));

        await vm.LoadAsync();
        vm.CustomStartDate = new DateTime(2026, 1, 2);
        vm.CustomEndDate = new DateTime(2026, 1, 2);
        await Task.Yield();

        Assert.Equal("Custom", vm.ActivePeriod);
        Assert.Equal([20d], ChartValues(vm));
    }

    private static List<double> ChartValues(PortfolioHistoryViewModel vm)
    {
        var series = Assert.IsType<LineSeries<DateTimePoint>>(Assert.Single(vm.ValueSeries));
        return series.Values!.Select(p => p.Value!.Value).ToList();
    }

    private sealed class StubHistoryQuery(IReadOnlyList<PortfolioDailySnapshot> snapshots)
        : IPortfolioHistoryQueryService
    {
        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
            Task.FromResult(snapshots);
    }

    private sealed class FakeSettings(string baseCurrency) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new(BaseCurrency: baseCurrency);
        public event Action? Changed;

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class StubFx(Dictionary<(string From, string To), decimal> rates)
        : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(
            decimal amount,
            string from,
            string to,
            DateOnly asOf,
            CancellationToken ct = default)
        {
            return rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var rate)
                ? Task.FromResult<decimal?>(amount * rate)
                : Task.FromResult<decimal?>(null);
        }
    }
}
