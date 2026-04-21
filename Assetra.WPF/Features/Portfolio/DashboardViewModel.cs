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

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly PortfolioViewModel _portfolio;

    [ObservableProperty] private ISeries[] _tenDaySeries = [];
    [ObservableProperty] private ICartesianAxis[] _tenDayXAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _tenDayYAxes = [new Axis { IsVisible = false }];

    public DashboardViewModel(PortfolioViewModel portfolio, IThemeService? themeService = null)
    {
        _portfolio = portfolio;

        _portfolio.PropertyChanged += (_, e) =>
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
        };

        _portfolio.Positions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TopPositions));
        _portfolio.Trades.CollectionChanged    += (_, _) => OnPropertyChanged(nameof(RecentTrades));

        _portfolio.History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PortfolioHistoryViewModel.Snapshots))
                RefreshTenDayChart();
        };

        if (themeService is not null)
            themeService.ThemeChanged += _ => RefreshTenDayChart();

        RefreshTenDayChart();
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
    public decimal DebtRatioValue        => _portfolio.DebtRatioValue;
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
