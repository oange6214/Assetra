using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.StatusBar;

/// <summary>
/// Owns the bottom status bar for Assetra.
///
/// Original Stockra version relied on IMarketService for TAIEX/futures data.
/// Assetra is a personal portfolio tracker with no real-time market feed,
/// so this implementation shows a pure clock-driven market open/closed
/// indicator and the current local time.
///
/// TWSE core session: 09:00 – 13:30 (Mon–Fri, excluding Taiwan holidays —
/// holiday calendar intentionally left out; we only check weekday + clock).
/// </summary>
public partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MarketOpen = new(9, 0, 0);
    private static readonly TimeSpan MarketClose = new(13, 30, 0);

    private readonly CompositeDisposable _disposables = new();
    private readonly ILocalizationService _localization;

    [ObservableProperty] private bool _isMarketOpen;
    [ObservableProperty] private string _marketStatusText = string.Empty;
    [ObservableProperty] private string _clockText = string.Empty;

    public StatusBarViewModel(
        IScheduler uiScheduler,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentNullException.ThrowIfNull(localization);

        _localization = localization;

        UpdateStatus(DateTime.Now);

        localization.LanguageChanged += OnLanguageChanged;

        // Tick every second to refresh clock + market status.
        Observable.Interval(TimeSpan.FromSeconds(1), uiScheduler)
            .Subscribe(_ => UpdateStatus(DateTime.Now))
            .DisposeWith(_disposables);
    }

    private void UpdateStatus(DateTime now)
    {
        ClockText = now.ToString("HH:mm:ss");
        IsMarketOpen = IsTwseOpen(now);
        MarketStatusText = IsMarketOpen
            ? _localization.Get("StatusBar.MarketOpen", "開盤中")
            : _localization.Get("StatusBar.MarketClosed", "休市");
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        UpdateStatus(DateTime.Now);

    private static bool IsTwseOpen(DateTime now)
    {
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;
        var tod = now.TimeOfDay;
        return tod >= MarketOpen && tod <= MarketClose;
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _disposables.Dispose();
    }
}
