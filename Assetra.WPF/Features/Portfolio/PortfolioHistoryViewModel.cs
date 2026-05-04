using System.Globalization;
using System.Windows.Media;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Provides chart data, period selection, and day-change metrics for the portfolio
/// history panel.  Owned by <see cref="PortfolioViewModel"/> as a child ViewModel.
/// </summary>
public sealed partial class PortfolioHistoryViewModel : ObservableObject
{
    private const int AllPeriodDays = 0;

    private readonly IPortfolioHistoryQueryService _historyQueryService;
    private readonly ILocalizationService _localization;
    private readonly IAppSettingsService? _settings;
    private readonly IMultiCurrencyValuationService? _fx;

    /// <summary>Full snapshot history (all dates), cached on each DB load.</summary>
    private IReadOnlyList<PortfolioDailySnapshot> _allSnapshots = [];

    /// <summary>Full snapshot history exposed for Dashboard 10-day chart.</summary>
    public IReadOnlyList<PortfolioDailySnapshot> Snapshots => _allSnapshots;

    // Chart series
    [ObservableProperty] private ISeries[] _valueSeries = [];
    [ObservableProperty] private ICartesianAxis[] _xAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _yAxes = [new Axis { IsVisible = false }];

    // Period selection
    [ObservableProperty] private int _selectedDays = 30;

    // Custom range (overrides SelectedDays when both ends are set)
    [ObservableProperty] private DateTime? _customStartDate;
    [ObservableProperty] private DateTime? _customEndDate;

    /// <summary>
    /// Tag of the currently-active preset ("30"/"90"/"180"/"365"/"All"), or
    /// "Custom" when both ends of the custom range are set. Drives the active
    /// state of the Trends preset buttons.
    /// </summary>
    public string ActivePeriod =>
        (CustomStartDate, CustomEndDate) is ({ }, { })
            ? "Custom"
            : SelectedDays == AllPeriodDays
                ? "All"
                : SelectedDays.ToString(CultureInfo.InvariantCulture);

    partial void OnSelectedDaysChanged(int value) => OnPropertyChanged(nameof(ActivePeriod));

    partial void OnCustomStartDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ActivePeriod));
        RefreshChart();
    }

    partial void OnCustomEndDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ActivePeriod));
        RefreshChart();
    }

    // Visibility guards
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private bool _isChartVisible = true;
    [ObservableProperty] private bool _isHistoryPanelVisible;

    partial void OnHasHistoryChanged(bool value) => IsHistoryPanelVisible = HasHistory && IsChartVisible;
    partial void OnIsChartVisibleChanged(bool value) => IsHistoryPanelVisible = HasHistory && IsChartVisible;

    public PortfolioHistoryViewModel(
        IPortfolioHistoryQueryService historyQueryService,
        ILocalizationService? localization = null,
        IAppSettingsService? settings = null,
        IMultiCurrencyValuationService? fx = null)
    {
        _historyQueryService = historyQueryService;
        _localization = localization ?? NullLocalizationService.Instance;
        _settings = settings;
        _fx = fx;
    }

    // Public API

    /// <summary>Fetches snapshots from DB and rebuilds the chart.</summary>
    public async Task LoadAsync()
    {
        _allSnapshots = await _historyQueryService.GetSnapshotsAsync();
        OnPropertyChanged(nameof(Snapshots));
        await RefreshChartAsync();
    }

    /// <summary>
    /// Called after a theme switch.  Rebuilds the chart series with fresh
    /// SkiaSharp colours read from the updated WPF resource dictionary.
    /// Does NOT hit the DB.
    /// </summary>
    public void OnThemeChanged() => RefreshChart();

    // Period command

    [RelayCommand]
    private async Task ChangePeriod(string? period)
    {
        if (string.Equals(period, "All", StringComparison.OrdinalIgnoreCase))
        {
            CustomStartDate = null;
            CustomEndDate = null;
            SelectedDays = AllPeriodDays;
            await RefreshChartAsync();
            return;
        }

        if (int.TryParse(period, out var days) && days > 0)
        {
            // Selecting a preset clears any custom range.
            CustomStartDate = null;
            CustomEndDate = null;
            SelectedDays = days;
            await RefreshChartAsync();
        }
    }

    // Chart building

    private async Task RefreshChartAsync()
    {
        var filtered = (CustomStartDate, CustomEndDate) is ({ } s, { } e)
            ? FilterByRange(_allSnapshots, s, e)
            : FilterByDays(_allSnapshots, SelectedDays);
        var points = await BuildPointsAsync(filtered);
        BuildChart(points);
    }

    private void RefreshChart()
    {
        AsyncHelpers.SafeFireAndForget(RefreshChartAsync, "PortfolioHistory.RefreshChart");
    }

    private static IReadOnlyList<PortfolioDailySnapshot> FilterByDays(
        IReadOnlyList<PortfolioDailySnapshot> all, int days)
    {
        if (days == AllPeriodDays)
            return all.OrderBy(s => s.SnapshotDate).ToList();

        if (all.Count == 0)
            return [];

        var latestSnapshotDate = all.Max(s => s.SnapshotDate);
        var cutoff = latestSnapshotDate.AddDays(-(days - 1));
        return all
            .Where(s => s.SnapshotDate >= cutoff)
            .OrderBy(s => s.SnapshotDate)
            .ToList();
    }

    private static IReadOnlyList<PortfolioDailySnapshot> FilterByRange(
        IReadOnlyList<PortfolioDailySnapshot> all, DateTime start, DateTime end)
    {
        var (lo, hi) = start <= end ? (start, end) : (end, start);
        var loDate = DateOnly.FromDateTime(lo);
        var hiDate = DateOnly.FromDateTime(hi);
        return all
            .Where(s => s.SnapshotDate >= loDate && s.SnapshotDate <= hiDate)
            .OrderBy(s => s.SnapshotDate)
            .ToList();
    }

    private async Task<IReadOnlyList<DateTimePoint>> BuildPointsAsync(
        IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        var points = new List<DateTimePoint>(snapshots.Count);
        foreach (var snapshot in snapshots.OrderBy(s => s.SnapshotDate))
        {
            var value = await ConvertMarketValueToBaseAsync(snapshot);
            if (value is null)
                continue;

            points.Add(new DateTimePoint(
                snapshot.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                (double)value.Value));
        }
        return points;
    }

    private async Task<decimal?> ConvertMarketValueToBaseAsync(PortfolioDailySnapshot snapshot)
    {
        var baseCurrency = _settings?.Current.BaseCurrency;
        if (_fx is null || string.IsNullOrWhiteSpace(baseCurrency))
            return snapshot.MarketValue;

        var fromCurrency = string.IsNullOrWhiteSpace(snapshot.Currency) ? "TWD" : snapshot.Currency;
        if (string.Equals(fromCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return snapshot.MarketValue;

        try
        {
            return await _fx.ConvertAsync(
                snapshot.MarketValue,
                fromCurrency,
                baseCurrency,
                snapshot.SnapshotDate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private void BuildChart(IReadOnlyList<DateTimePoint> points)
    {
        HasHistory = points.Count >= 1;
        if (points.Count == 0)
        {
            ValueSeries = [];
            return;
        }

        // Read theme colours fresh each time so the chart always matches
        // the current palette (Dark / Light / colour-scheme).
        var accentColor = GetSkColor("AppAccent", "#0078D4");
        var fillColor = accentColor.WithAlpha(32);
        var labelColor = GetSkColor("AppTextSecondary", "#787B86");
        var separatorColor = GetSkColor("AppBorderLight", "#2E2E2E");

        ValueSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values            = points,
                Name              = GetString("Portfolio.History.MarketValue", "Market Value"),
                Stroke            = new SolidColorPaint(accentColor, 2),
                Fill              = new SolidColorPaint(fillColor),
                GeometrySize      = 4,
                GeometryFill      = new SolidColorPaint(accentColor),
                GeometryStroke    = new SolidColorPaint(accentColor, 1),
                LineSmoothness    = 0,
                AnimationsSpeed   = TimeSpan.Zero,
            }
        ];

        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MM/dd"))
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
            }
        ];

        YAxes =
        [
            new Axis
            {
                Position        = LiveChartsCore.Measure.AxisPosition.End,
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
                Labeler         = v => v.ToString("N0"),
            }
        ];
    }

    // Colour helpers

    /// <summary>
    /// Reads a <see cref="SolidColorBrush"/> from the WPF application resources and
    /// converts it to an <see cref="SKColor"/>.  Falls back to <paramref name="hexFallback"/>
    /// if the resource is not found (e.g., in unit-test contexts without a UI).
    /// </summary>
    private static SKColor GetSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);
}
