using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Fx;

/// <summary>
/// Orchestrator that walks each (foreign, base) currency pair, pulls
/// historical FX from <see cref="IFxRateHistoryFetcher"/>, and upserts into
/// <see cref="IFxRateHistoryRepository"/>. Called from app startup so the
/// store is populated before the first balance-sheet render asks for rates.
///
/// <para>Failure model: per-pair errors are absorbed inside the fetcher (it
/// returns empty list rather than throwing). The orchestrator additionally
/// catches the unexpected and logs nothing — UI doesn't surface FX backfill
/// as a user-facing event; the Reports FX warnings banner is the visible
/// signal when something's missing.</para>
/// </summary>
public sealed class FxRateHistoryRefresher
{
    /// <summary>
    /// Currencies we try to backfill when AppSettings doesn't specify a list.
    /// Covers the major TWD-pair quotes Assetra historically supports.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultForeignCurrencies =
        new[] { "USD", "JPY", "HKD", "EUR" };

    private readonly IFxRateHistoryFetcher _fetcher;
    private readonly IFxRateHistoryRepository _repo;
    private readonly IAppSettingsService? _settings;

    public FxRateHistoryRefresher(
        IFxRateHistoryFetcher fetcher,
        IFxRateHistoryRepository repo,
        IAppSettingsService? settings = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _settings = settings;
    }

    /// <summary>
    /// Refresh historical FX rates for each foreign currency against the base.
    /// </summary>
    /// <param name="baseCurrency">Target base (e.g. "TWD").</param>
    /// <param name="foreignCurrencies">Pairs to fetch (e.g. ["USD","JPY","HKD"]).
    /// Same-currency entries are silently skipped.</param>
    /// <param name="daysBack">How far back to fetch (default 7).</param>
    public async Task RefreshAsync(
        string baseCurrency,
        IReadOnlyCollection<string> foreignCurrencies,
        int daysBack = 7,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency)) return;
        if (foreignCurrencies is null || foreignCurrencies.Count == 0) return;

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-Math.Max(1, daysBack));
        var baseUpper = baseCurrency.Trim().ToUpperInvariant();

        var collected = new List<FxRateHistoryEntry>();
        foreach (var raw in foreignCurrencies)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var foreign = raw.Trim().ToUpperInvariant();
            if (string.Equals(foreign, baseUpper, StringComparison.Ordinal)) continue;

            try
            {
                var entries = await _fetcher.FetchAsync(foreign, baseUpper, from, to, ct).ConfigureAwait(false);
                if (entries.Count > 0) collected.AddRange(entries);
            }
            catch
            {
                // Defense-in-depth: fetcher already swallows; this is the second
                // guard in case a future fetcher impl leaks an exception.
            }
        }

        if (collected.Count > 0)
        {
            try
            {
                await _repo.UpsertRangeAsync(collected, ct).ConfigureAwait(false);
                await UpdateLastRefreshTimestampAsync().ConfigureAwait(false);
            }
            catch
            {
                // DB hiccup during backfill is non-fatal — next startup tries again.
            }
        }
    }

    /// <summary>
    /// Persist <see cref="AppSettings.LastFxRefreshAt"/> = now so the settings
    /// page can surface "上次更新" to the user. Failures swallowed — the
    /// timestamp is purely informational; missing it just means stale UI.
    /// </summary>
    private async Task UpdateLastRefreshTimestampAsync()
    {
        if (_settings is null) return;
        try
        {
            var current = _settings.Current ?? new AppSettings();
            var updated = current with { LastFxHistoryRefreshAt = DateTimeOffset.UtcNow };
            await _settings.SaveAsync(updated).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort timestamp; ignore errors.
        }
    }
}
