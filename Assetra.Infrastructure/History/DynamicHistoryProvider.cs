using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.FinMind;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.History;

/// <summary>
/// Delegates to the correct provider based on the live AppSettings value,
/// so the user can switch between TWSE / Yahoo Finance / FinMind without restarting.
/// </summary>
internal sealed class DynamicHistoryProvider : IStockHistoryProvider
{
    private readonly TwseHistoryProvider _twse;
    private readonly YahooFinanceHistoryProvider _yahoo;
    private readonly FinMindHistoryProvider _finMind;
    private readonly FugleHistoryProvider _fugle;
    private readonly IAppSettingsService _settings;

    public DynamicHistoryProvider(HttpClient http, IAppSettingsService settings,
        FinMindService finMindService, FinMindApiStatus finMindStatus, FugleClient fugleClient)
    {
        _twse = new TwseHistoryProvider(http);
        _yahoo = new YahooFinanceHistoryProvider(http);
        _finMind = new FinMindHistoryProvider(http, finMindService, finMindStatus);
        _fugle = new FugleHistoryProvider(fugleClient);
        _settings = settings;
    }

    public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
    {
        IStockHistoryProvider active = _settings.Current.HistoryProvider switch
        {
            "fugle" => _fugle,
            "yahoo" => _yahoo,
            "finmind" => _finMind,
            _ => _twse,
        };

        return active.GetHistoryAsync(symbol, exchange, period, ct);
    }
}
