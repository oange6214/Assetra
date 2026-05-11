using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly PortfolioViewModel _portfolio;
    private readonly IThemeService? _themeService;
    private readonly Action<ApplicationTheme>? _themeChangedHandler;

    [ObservableProperty] private ISeries[] _tenDaySeries = [];
    [ObservableProperty] private ICartesianAxis[] _tenDayXAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _tenDayYAxes = [new Axis { IsVisible = false }];

    /// <summary>
    /// 純無軸 sparkline axes — 給 InvestmentFocusWidget 使用。chart 不顯示
    /// 標籤 / 刻度 / 分隔線，視覺只有那條曲線。
    /// </summary>
    public ICartesianAxis[] SparklineXAxes { get; } = [new Axis { IsVisible = false }];
    public ICartesianAxis[] SparklineYAxes { get; } = [new Axis { IsVisible = false }];

    /// <summary>
    /// L3 — writeback channel for tab navigation. Defaults to the concrete
    /// <see cref="PortfolioViewModel"/> instance (which implements
    /// <see cref="Contracts.IDashboardNavigation"/>) so existing wiring keeps
    /// working; tests can inject a stub.
    /// </summary>
    private readonly Contracts.IDashboardNavigation _navigation;

    public DashboardViewModel(
        PortfolioViewModel portfolio,
        IThemeService? themeService = null,
        Contracts.IDashboardNavigation? navigation = null)
    {
        _portfolio = portfolio;
        _themeService = themeService;
        _navigation = navigation ?? portfolio;

        _portfolio.PropertyChanged += OnPortfolioPropertyChanged;
        ((INotifyCollectionChanged)_portfolio.Positions).CollectionChanged += OnPositionsChanged;
        ((INotifyCollectionChanged)_portfolio.Trades).CollectionChanged    += OnTradesChanged;
        _portfolio.History.PropertyChanged     += OnHistoryPropertyChanged;

        if (themeService is not null)
        {
            _themeChangedHandler = _ => RefreshTenDayChart();
            themeService.ThemeChanged += _themeChangedHandler;
        }

        RefreshTenDayChart();
    }

    private void OnPortfolioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        switch (e.PropertyName)
        {
            case nameof(PortfolioViewModel.NetWorth):
                OnPropertyChanged(nameof(NetWorthDisplay));
                OnPropertyChanged(nameof(LeverageRatio));
                break;
            case nameof(PortfolioViewModel.TotalMarketValue):
                OnPropertyChanged(nameof(TotalMarketValueDisplay));
                break;
            case nameof(PortfolioViewModel.TotalCost):
                OnPropertyChanged(nameof(TotalCostDisplay));
                break;
            case nameof(PortfolioViewModel.TotalPnl):
                OnPropertyChanged(nameof(TotalPnlDisplay));
                break;
            case nameof(PortfolioViewModel.TotalAssets):
                OnPropertyChanged(nameof(TotalAssetsDisplay));
                OnPropertyChanged(nameof(LeverageRatio));
                break;
            case nameof(PortfolioViewModel.TotalLiabilities):
                OnPropertyChanged(nameof(TotalLiabilitiesDisplay));
                break;
            case nameof(PortfolioViewModel.DayPnl):
                OnPropertyChanged(nameof(DayPnlDisplay));
                OnPropertyChanged(nameof(SignedDayPnlDisplay));
                break;
        }
    }

    private void OnPositionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(TopPositions));

    private void OnTradesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(RecentTrades));

    private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PortfolioHistoryViewModel.Snapshots))
            RefreshTenDayChart();
    }

    public void Dispose()
    {
        // C2 leak fix: anonymous-lambda subscriptions can't be detached;
        // refactored to named handlers so Dispose actually unsubscribes.
        _portfolio.PropertyChanged -= OnPortfolioPropertyChanged;
        ((INotifyCollectionChanged)_portfolio.Positions).CollectionChanged -= OnPositionsChanged;
        ((INotifyCollectionChanged)_portfolio.Trades).CollectionChanged    -= OnTradesChanged;
        _portfolio.History.PropertyChanged     -= OnHistoryPropertyChanged;
        if (_themeService is not null && _themeChangedHandler is not null)
            _themeService.ThemeChanged -= _themeChangedHandler;
    }

    // Hero card
    public decimal NetWorth             => _portfolio.NetWorth;
    public string  NetWorthDisplay      => $"NT${NetWorth:N0}";
    public decimal DayPnl               => _portfolio.DayPnl;
    public string  DayPnlDisplay        => $"NT${Math.Abs(DayPnl):N0}";
    public string  SignedDayPnlDisplay  => DayPnl >= 0 ? $"+NT${DayPnl:N0}" : $"-NT${Math.Abs(DayPnl):N0}";
    public string  DayPnlPercentDisplay => _portfolio.DayPnlPercentDisplay;
    public bool    IsDayPnlPositive     => _portfolio.IsDayPnlPositive;
    public bool    HasDayPnl            => _portfolio.HasDayPnl;

    // Investment metric cards
    public decimal TotalMarketValue      => _portfolio.TotalMarketValue;
    public string  TotalMarketValueDisplay => $"NT${TotalMarketValue:N0}";
    public decimal TotalCost             => _portfolio.TotalCost;
    public string  TotalCostDisplay      => $"NT${TotalCost:N0}";
    public decimal TotalPnl              => _portfolio.TotalPnl;
    public string  TotalPnlDisplay       => $"NT${TotalPnl:N0}";
    public decimal TotalPnlPercent       => _portfolio.TotalPnlPercent;
    public bool    IsTotalPositive       => _portfolio.IsTotalPositive;

    // Global financial metrics. Kept for compatibility, but the investment
    // dashboard no longer binds to them; FinancialOverview owns that story.
    public decimal TotalAssets           => _portfolio.TotalAssets;
    public string  TotalAssetsDisplay    => $"NT${TotalAssets:N0}";
    public decimal TotalLiabilities      => _portfolio.TotalLiabilities;
    public string  TotalLiabilitiesDisplay => $"NT${TotalLiabilities:N0}";
    public decimal DebtRatioValue        => _portfolio.Financial.DebtRatioValue;
    public decimal LeverageRatio         => _portfolio.NetWorth > 0
        ? _portfolio.TotalAssets / _portfolio.NetWorth
        : 0m;

    // Collections
    public IEnumerable<PortfolioRowViewModel> TopPositions =>
        _portfolio.Positions
                  .OrderByDescending(p => p.MarketValue)
                  .Take(5);

    public IEnumerable<TradeRowViewModel> RecentTrades =>
        _portfolio.Trades
                  .OrderByDescending(t => t.TradeDate)
                  .Take(5);

    // Navigation
    [RelayCommand]
    private void NavigateToPositions() => _navigation.NavigateTo(PortfolioTab.Positions);

    [RelayCommand]
    private void NavigateToTrades() => _navigation.NavigateTo(PortfolioTab.Trades);

    // Chart
    // Long-term refactor: Portfolio.Dashboard tab 移除後，這個 series 改為
    // InvestmentFocusWidget 的 30 天 sparkline 用。屬性名稱保留 TenDay* 以
    // 避免大規模 rename；cutoff 由 -9 改 -29（10 天 → 30 天）。
    //
    // v0.30+ daily NW snapshot：若 snapshot 有 CashValue + LiabilityValue
    // 欄位，sparkline 顯示「真實淨值」(Cash + Equity − Liability)；fallback
    // 為 MarketValue（投資組合市值）。每張快照逐個檢查（migrating 期間混用）。
    private void RefreshTenDayChart()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-29));
        var snapshots = _portfolio.History.Snapshots
            .Where(s => s.SnapshotDate >= cutoff)
            .OrderBy(s => s.SnapshotDate)
            .ToList();

        if (snapshots.Count == 0)
        {
            TenDaySeries = [];
            return;
        }

        var accentColor    = GetSkColor("AppAccent",        "#0078D4");
        var fillColor      = accentColor.WithAlpha(32);
        var labelColor     = GetSkColor("AppTextSecondary", "#9E9E9E");
        var separatorColor = GetSkColor("AppBorderLight",   "#2E2E2E");

        // 若 snapshot 有 NW 三組件，使用 Cash + Equity − Liability；否則 fallback MV
        var points = snapshots
            .Select(s => new DateTimePoint(
                s.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                (double)ResolveNetWorthValue(s)))
            .ToList();

        TenDaySeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values          = points,
                Stroke          = new SolidColorPaint(accentColor, 2),
                Fill            = new SolidColorPaint(fillColor),
                GeometrySize    = 4,
                GeometryFill    = new SolidColorPaint(accentColor),
                GeometryStroke  = new SolidColorPaint(accentColor, 1),
                LineSmoothness  = 0,
                AnimationsSpeed = TimeSpan.Zero,
            }
        ];

        TenDayXAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("MM/dd"))
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = null,
                TicksPaint      = null,
            }
        ];

        TenDayYAxes =
        [
            new Axis
            {
                Position        = LiveChartsCore.Measure.AxisPosition.End,
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
                Labeler         = v => $"NT${v / 1_000_000:F1}M",
            }
        ];
    }

    /// <summary>
    /// 從 snapshot 解出「淨值」— 若有 CashValue + EquityValue + LiabilityValue
    /// 三個欄位（v0.30+ daily NW snapshot），用 Cash + Equity − Liability；
    /// 否則 fallback 為 MarketValue（舊版只記投資 MV，作為 proxy）。
    /// </summary>
    private static decimal ResolveNetWorthValue(Assetra.Core.Models.PortfolioDailySnapshot s)
    {
        if (s.CashValue.HasValue && s.EquityValue.HasValue)
        {
            var equity = s.EquityValue.Value;
            var cash = s.CashValue.Value;
            var liab = s.LiabilityValue ?? 0m;
            return cash + equity - liab;
        }
        return s.MarketValue;
    }

    private static SKColor GetSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }
}
