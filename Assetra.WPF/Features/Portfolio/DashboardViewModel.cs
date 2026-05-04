using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Appearance;
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

    public BudgetSummaryCardViewModel? BudgetSummary { get; }

    public DashboardViewModel(
        PortfolioViewModel portfolio,
        IThemeService? themeService = null,
        BudgetSummaryCardViewModel? budgetSummary = null)
    {
        _portfolio = portfolio;
        _themeService = themeService;
        BudgetSummary = budgetSummary;
        if (BudgetSummary is not null)
        {
            _portfolio.Trades.CollectionChanged += OnTradesChangedForBudget;
            AsyncHelpers.SafeFireAndForget(BudgetSummary.LoadAsync, "BudgetSummary.LoadFromDashboard");
        }

        _portfolio.PropertyChanged += OnPortfolioPropertyChanged;
        _portfolio.Positions.CollectionChanged += OnPositionsChanged;
        _portfolio.Trades.CollectionChanged    += OnTradesChanged;
        _portfolio.History.PropertyChanged     += OnHistoryPropertyChanged;

        if (themeService is not null)
        {
            _themeChangedHandler = _ => RefreshTenDayChart();
            themeService.ThemeChanged += _themeChangedHandler;
        }

        RefreshTenDayChart();
    }

    private void OnTradesChangedForBudget(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (BudgetSummary is not null)
            AsyncHelpers.SafeFireAndForget(BudgetSummary.LoadAsync, "BudgetSummary.LoadFromDashboard");
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
        if (BudgetSummary is not null)
            _portfolio.Trades.CollectionChanged -= OnTradesChangedForBudget;
        _portfolio.PropertyChanged -= OnPortfolioPropertyChanged;
        _portfolio.Positions.CollectionChanged -= OnPositionsChanged;
        _portfolio.Trades.CollectionChanged    -= OnTradesChanged;
        _portfolio.History.PropertyChanged     -= OnHistoryPropertyChanged;
        if (_themeService is not null && _themeChangedHandler is not null)
            _themeService.ThemeChanged -= _themeChangedHandler;
    }

    // Hero card
    public decimal NetWorth             => _portfolio.NetWorth;
    public string  NetWorthDisplay      => $"NT${NetWorth:N0}";
    public decimal DayPnl               => _portfolio.DayPnl;
    public string  DayPnlDisplay        => $"NT${Math.Abs(DayPnl):N0}";
    public string  DayPnlPercentDisplay => _portfolio.DayPnlPercentDisplay;
    public bool    IsDayPnlPositive     => _portfolio.IsDayPnlPositive;
    public bool    HasDayPnl            => _portfolio.HasDayPnl;

    // Metric cards
    public decimal TotalPnl              => _portfolio.TotalPnl;
    public string  TotalPnlDisplay       => $"NT${TotalPnl:N0}";
    public decimal TotalPnlPercent       => _portfolio.TotalPnlPercent;
    public bool    IsTotalPositive       => _portfolio.IsTotalPositive;
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
    private void NavigateToPositions() => _portfolio.SelectedTab = PortfolioTab.Positions;

    [RelayCommand]
    private void NavigateToTrades() => _portfolio.SelectedTab = PortfolioTab.Trades;

    // Chart
    private void RefreshTenDayChart()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-9));
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

        var points = snapshots
            .Select(s => new DateTimePoint(
                s.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                (double)s.MarketValue))
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
