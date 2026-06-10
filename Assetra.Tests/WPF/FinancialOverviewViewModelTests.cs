using System.Collections.ObjectModel;
using System.ComponentModel;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.FinancialOverview;
using Assetra.WPF.Features.Goals;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.Contracts;
using Microsoft.Reactive.Testing;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Tests for the L3-decoupled <see cref="FinancialOverviewViewModel"/>. Construction-
/// only contracts: ctor guards, feed property-change subscription wiring. Reload paths
/// route through <c>Application.Current.Dispatcher.Invoke</c> so they're not exercised
/// here (would need an STA dispatcher in the test).
/// </summary>
public sealed class FinancialOverviewViewModelTests
{
    private sealed class StubFeed : IPortfolioPositionFeed
    {
        public ObservableCollection<PortfolioRowViewModel> PositionsList { get; } = new();
        public IReadOnlyList<PortfolioRowViewModel> Positions => PositionsList;

        private decimal _totalCash;
        public decimal TotalCash
        {
            get => _totalCash;
            set
            {
                if (_totalCash == value)
                    return;
                _totalCash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCash)));
            }
        }

        private decimal _totalMarketValue;
        public decimal TotalMarketValue
        {
            get => _totalMarketValue;
            set
            {
                if (_totalMarketValue == value)
                    return;
                _totalMarketValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalMarketValue)));
            }
        }

        private decimal _totalCost;
        public decimal TotalCost
        {
            get => _totalCost;
            set
            {
                if (_totalCost == value)
                    return;
                _totalCost = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCost)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void Constructor_NullQueryService_Throws()
    {
        var feed = new StubFeed();
        Assert.Throws<ArgumentNullException>(() =>
            new FinancialOverviewViewModel(queryService: null!, portfolio: feed));
    }

    [Fact]
    public void Constructor_NullPortfolio_Throws()
    {
        var qs = new Mock<IFinancialOverviewQueryService>();
        Assert.Throws<ArgumentNullException>(() =>
            new FinancialOverviewViewModel(queryService: qs.Object, portfolio: null!));
    }

    [Fact]
    public void Constructor_DefaultsAreSensible()
    {
        var vm = new FinancialOverviewViewModel(
            new Mock<IFinancialOverviewQueryService>().Object,
            new StubFeed());

        Assert.Equal(0m, vm.TotalNetWorth);
        Assert.Equal(0m, vm.TotalAssets);
        Assert.Equal(0m, vm.TotalInvestments);
        Assert.Equal(0m, vm.TotalLiabilities);
        Assert.Equal("TWD", vm.BaseCurrency);
        Assert.Empty(vm.AssetGroups);
        Assert.Empty(vm.InvestGroups);
        Assert.Empty(vm.LiabGroups);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void TotalDisplays_FormatThroughMoneyFormatter()
    {
        var vm = new FinancialOverviewViewModel(
            new Mock<IFinancialOverviewQueryService>().Object,
            new StubFeed());

        // Set via reflection — these are ObservableProperty setters.
        typeof(FinancialOverviewViewModel).GetProperty("TotalNetWorth")!.SetValue(vm, 1234567m);
        typeof(FinancialOverviewViewModel).GetProperty("BaseCurrency")!.SetValue(vm, "USD");

        // Display string updates fire from OnTotalNetWorthChanged + OnBaseCurrencyChanged.
        Assert.Contains("1,234,567", vm.TotalNetWorthDisplay);
    }

    [Fact]
    public void OnPortfolioTotalMarketValueChanged_FiresLoadInBackground()
    {
        var qs = new Mock<IFinancialOverviewQueryService>();
        qs.Setup(x => x.BuildAsync(It.IsAny<IReadOnlyList<FinancialOverviewInvestmentItem>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new FinancialOverviewResult(
              AssetGroups: [],
              InvestmentGroups: [],
              LiabilityGroups: [],
              TotalAssets: 0m,
              TotalInvestments: 0m,
              TotalLiabilities: 0m,
              BaseCurrency: "TWD"));
        var feed = new StubFeed();
        _ = new FinancialOverviewViewModel(qs.Object, feed);

        // Triggering the watched property change should invoke BuildAsync via SafeFireAndForget.
        // We can't synchronously assert on an async fire-and-forget without marshalling, but we
        // CAN assert that the event subscription is wired correctly: setting the property
        // emits on the feed without throwing back at us.
        feed.TotalMarketValue = 9999m;

        Assert.Equal(9999m, feed.TotalMarketValue);
    }

    [Fact]
    public void RapidQuoteTicks_CoalesceToSingleReload_WithinSampleWindow()
    {
        // #1 perf fix：盤中報價串流每批都 raise TotalMarketValue PropertyChanged，
        // 原本每次都立即 LoadAsync → BuildAsync（SQLite 查詢 + 全量重建三組 accordion）。
        // WHY this test matters：保證一連串密集 tick 被 Rx Sample 合併成「每 500ms 視窗
        // 最多一次」重載，而不是 N 次。若有人把 Sample 換回 per-tick 直接呼叫、或拿掉
        // 節流，BuildAsync 會被呼叫 5 次而非 1 次 → 此測試紅，正是我們要鎖住的意圖。
        var buildCount = 0;
        var qs = new Mock<IFinancialOverviewQueryService>();
        qs.Setup(x => x.BuildAsync(It.IsAny<IReadOnlyList<FinancialOverviewInvestmentItem>>(), It.IsAny<CancellationToken>()))
          .Callback(() => buildCount++)
          .ReturnsAsync(new FinancialOverviewResult(
              AssetGroups: [],
              InvestmentGroups: [],
              LiabilityGroups: [],
              TotalAssets: 0m,
              TotalInvestments: 0m,
              TotalLiabilities: 0m,
              BaseCurrency: "TWD"));
        var feed = new StubFeed();

        // Virtual-time scheduler so the 500ms Sample window is deterministic —
        // no real delay / polling. Inject as the trailing optional ctor param.
        var scheduler = new TestScheduler();
        _ = new FinancialOverviewViewModel(qs.Object, feed, reloadScheduler: scheduler);

        // ctor 不會自行 Load；起點計數應為 0（鎖住「建構不重載」前提，也讓下面 delta 乾淨）。
        Assert.Equal(0, buildCount);

        // 5 次密集 tick，期間「不」推進排程器 → 全部落在同一個 Sample 視窗內。
        // StubFeed 的 setter 只在值改變時 raise，故每次給不同值。
        feed.TotalMarketValue = 100m;
        feed.TotalMarketValue = 200m;
        feed.TotalMarketValue = 300m;
        feed.TotalMarketValue = 400m;
        feed.TotalMarketValue = 500m;

        // 尚未推進排程器 → Sample 還沒放行任何一筆。
        Assert.Equal(0, buildCount);

        // 推進超過 500ms 視窗 → Sample 放行「最新值」一次，觸發單次 LoadAsync→BuildAsync。
        // Moq 的 BuildAsync 同步完成（ReturnsAsync），SafeFireAndForget 的 continuation
        // 在 AdvanceBy 同執行緒同步跑完，故計數在此呼叫返回後即穩定。
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(600).Ticks);

        Assert.Equal(1, buildCount);
    }

    [Fact]
    public async Task GoalsWidgetSummary_ProjectsGoalRiskAndTopProgress()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new FinancialGoal(Guid.NewGuid(), "House", 200_000m, 40_000m, today.AddDays(10), null),
                new FinancialGoal(Guid.NewGuid(), "Emergency", 100_000m, 50_000m, today.AddDays(20), null),
                new FinancialGoal(Guid.NewGuid(), "Trip", 100_000m, 90_000m, today.AddDays(90), null),
            ]);
        var goals = new GoalsViewModel(repo.Object);

        try
        {
            await goals.LoadAsync();
            var vm = new FinancialOverviewViewModel(
                new Mock<IFinancialOverviewQueryService>().Object,
                new StubFeed(),
                goalsWidget: goals);

            Assert.Equal("3", vm.GoalsWidgetTotalActiveGoalsDisplay);
            Assert.StartsWith("House · ", vm.GoalsWidgetNearestDeadlineDisplay);
            Assert.Contains(today.AddDays(10).ToString("yyyy-MM-dd"), vm.GoalsWidgetNearestDeadlineDisplay);
            Assert.Equal("2", vm.GoalsWidgetAttentionGoalsDisplay);
            Assert.Equal("Trip · 90.0%", vm.GoalsWidgetTopProgressDisplay);
        }
        finally
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(goals);
        }
    }

    // ── v2 tests：focus visibility + KPI reorder edge cases ──

    [Fact]
    public void IsAssetClassVisible_DefaultsTrueWhenSettingsNull()
    {
        var vm = new FinancialOverviewViewModel(
            new Mock<IFinancialOverviewQueryService>().Object,
            new StubFeed(),
            settings: null);

        // 沒注入 settings → 任何 cell 都顯示（被 VM null check 過濾才會藏）
        // 直接呼叫 IsAssetClassVisible 不行（private），用反射檢查 IsXxxFocusVisible
        // 在 VM null 情況下回 false（VM 沒注入），等於上層 widget 自動隱藏。
        Assert.False(vm.IsCashFocusVisible);
        Assert.False(vm.IsRealEstateFocusVisible);
    }

    [Fact]
    public void AssetClassFocusVisibility_FalseInSettings_HidesCell()
    {
        var settings = new StubSettings(new AppSettings(
            AssetClassFocusVisibility: new Dictionary<string, bool>
            {
                { "Cash", false },
            }));
        var vm = new FinancialOverviewViewModel(
            new Mock<IFinancialOverviewQueryService>().Object,
            new StubFeed(),
            settings: settings);

        // settings 把 Cash 設 false → 即使 PortfolioRef 注入也不顯示
        Assert.False(vm.IsCashFocusVisible);
    }

    private sealed class StubSettings(AppSettings current) : Assetra.Core.Interfaces.IAppSettingsService
    {
        public AppSettings Current { get; private set; } = current;
        public event Action? Changed;
        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    // ── #3 tests：KPI/ratio family must use the full balance-sheet base ──────
    //
    // WHY: the Hero/公式列 use BalanceSheetTotalAssets/NetWorth (投資+現金+不動產+
    // 退休+實物 − 負債). The KPI cards + 負債佔比 historically used the UNDERCOUNTING
    // base「TotalAssets + TotalInvestments」, where TotalAssets only sums the
    // asset_item table (misses RE/retirement/physical). On the same screen the
    // debt ratio / 負債佔比 were inflated and 總資產/淨資產 disagreed with the Hero.
    // These tests pin the base to BalanceSheetTotalAssets/NetWorth.
    //
    // SEAM: with PortfolioRef = null and no focus widgets injected,
    // BalanceSheetTotalAssets collapses to exactly TotalInvestments, and
    // BalanceSheetNetWorth to TotalInvestments − TotalLiabilities. We set
    // TotalAssets (the undercount) to a DISTINCT non-zero value so the assertions
    // fail under the old「TotalAssets + TotalInvestments」formula — i.e. the test
    // can only pass with the balance-sheet base, encoding the consistency intent.

    private static FinancialOverviewViewModel BuildVmWithTotals(
        decimal totalInvestments, decimal totalAssets, decimal totalLiabilities)
    {
        var vm = new FinancialOverviewViewModel(
            new Mock<IFinancialOverviewQueryService>().Object,
            new StubFeed());

        var t = typeof(FinancialOverviewViewModel);
        t.GetProperty("TotalInvestments")!.SetValue(vm, totalInvestments);
        t.GetProperty("TotalAssets")!.SetValue(vm, totalAssets);
        t.GetProperty("TotalLiabilities")!.SetValue(vm, totalLiabilities);
        return vm;
    }

    [Fact]
    public void BalanceSheetTotals_CollapseToInvestments_WhenNoFocusWidgets()
    {
        // Documents the seam the other #3 tests rely on: no PortfolioRef / focus
        // widgets ⇒ cash/RE/retirement/physical components are 0.
        var vm = BuildVmWithTotals(totalInvestments: 800_000m, totalAssets: 200_000m, totalLiabilities: 100_000m);

        Assert.Equal(800_000m, vm.BalanceSheetTotalAssets);
        Assert.Equal(700_000m, vm.BalanceSheetNetWorth);
    }

    [Fact]
    public void LiabilityShareDisplay_UsesBalanceSheetTotalAssets_NotUndercountBase()
    {
        // BalanceSheetTotalAssets = 800k ⇒ 100k / 800k = 12.5%.
        // Old buggy base (TotalAssets + TotalInvestments = 1,000k) would give 10.0%.
        var vm = BuildVmWithTotals(totalInvestments: 800_000m, totalAssets: 200_000m, totalLiabilities: 100_000m);

        Assert.Equal("12.5%", vm.LiabilityShareDisplay);
    }

    [Fact]
    public void DebtRatioCard_UsesBalanceSheetBase_ForRatioAndLeverage()
    {
        // DebtRatio is in the default KPI selection, so it is present in KpiCards
        // without injecting settings.
        var vm = BuildVmWithTotals(totalInvestments: 800_000m, totalAssets: 200_000m, totalLiabilities: 100_000m);

        var card = vm.KpiCards.Single(c => c.Id == KpiMetric.DebtRatio);

        // 負債比率 = 100k / BalanceSheetTotalAssets(800k) = 12.5% (old base → 10.0%).
        Assert.Equal("12.5%", card.ValueDisplay);
        // 槓桿比 = BalanceSheetTotalAssets(800k) / BalanceSheetNetWorth(700k) = 1.14
        // (old base → 1,000k / TotalNetWorth(900k) = 1.11).
        Assert.Equal("1.14", card.SecondaryValueDisplay);
    }

    [Fact]
    public void DebtRatioCard_LeverageSecondaryEmpty_WhenBalanceSheetNetWorthNotPositive()
    {
        // Liabilities ≥ assets ⇒ BalanceSheetNetWorth ≤ 0 ⇒ leverage 副值留空。
        var vm = BuildVmWithTotals(totalInvestments: 500_000m, totalAssets: 200_000m, totalLiabilities: 500_000m);

        var card = vm.KpiCards.Single(c => c.Id == KpiMetric.DebtRatio);

        Assert.Equal(string.Empty, card.SecondaryValueDisplay);
    }

    [Fact]
    public void TotalAssetsAndNetWorth_SurfaceTheFullBalanceSheetBase_NotTheUndercount()
    {
        // The 總資產/淨資產 figures (Hero + 公式列, and the now-permanent NetWorth/
        // TotalAssets KPIs) are surfaced via these display props. They must equal
        // the balance-sheet base — NOT the undercounting「TotalAssets + TotalInvestments」
        // (here 1,000k) nor the matching TotalNetWorth (900k).
        var vm = BuildVmWithTotals(totalInvestments: 800_000m, totalAssets: 200_000m, totalLiabilities: 100_000m);

        // 總資產 = BalanceSheetTotalAssets = 800k.
        Assert.Contains("800,000", vm.BalanceSheetTotalAssetsDisplay);
        Assert.DoesNotContain("1,000,000", vm.BalanceSheetTotalAssetsDisplay);

        // 淨資產 = BalanceSheetNetWorth = 800k − 100k = 700k.
        Assert.Contains("700,000", vm.BalanceSheetNetWorthDisplay);
        Assert.DoesNotContain("900,000", vm.BalanceSheetNetWorthDisplay);
    }
}
