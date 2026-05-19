using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.MarketData;

public sealed class EquityRouter : IEquityRouter
{
    private readonly IReadOnlyList<IEquityQuoteProvider> _providers;
    private readonly IEquityQuoteCache? _cache;
    private readonly TimeProvider _timeProvider;

    public EquityRouter(
        IEnumerable<IEquityQuoteProvider> providers,
        IEquityQuoteCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        _providers = providers?.ToList() ?? [];
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default)
    {
        return await GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh, ct).ConfigureAwait(false);
    }

    public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        TimeSpan maxAge,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var results = await GetQuotesAsync([key], maxAge, ct).ConfigureAwait(false);
        return results.FirstOrDefault()
            ?? MarketDataResult<EquityQuote>.Failure(Unsupported(key));
    }

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default)
    {
        return await GetQuotesAsync(keys, EquityQuoteCachePolicies.Fresh, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        TimeSpan maxAge,
        CancellationToken ct = default)
    {
        if (keys is null || keys.Count == 0)
            return [];

        var orderedKeys = keys.Distinct().ToList();
        var results = new Dictionary<EquityInstrumentKey, MarketDataResult<EquityQuote>>();
        var lastErrors = new Dictionary<EquityInstrumentKey, MarketDataError>();
        var pending = new List<EquityInstrumentKey>();
        var now = _timeProvider.GetUtcNow();

        foreach (var key in orderedKeys)
        {
            if (_cache is not null && _cache.TryGet(key, maxAge, now, out var cachedQuote))
                results[key] = MarketDataResult<EquityQuote>.Success(cachedQuote);
            else
                pending.Add(key);
        }

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            var handled = pending.Where(provider.CanHandle).ToList();
            if (handled.Count == 0)
                continue;

            IReadOnlyList<MarketDataResult<EquityQuote>> providerResults;
            try
            {
                providerResults = await provider.GetQuotesAsync(handled, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                foreach (var key in handled)
                    lastErrors[key] = ProviderUnavailable(provider.ProviderName, key);
                continue;
            }

            foreach (var result in providerResults)
            {
                var resultKey = result.Value?.Instrument ?? result.Error?.Instrument;
                if (resultKey is null)
                    continue;

                if (result.IsSuccess && result.Value is not null)
                {
                    results[resultKey] = result;
                    _cache?.Store(result.Value, now);
                    pending.Remove(resultKey);
                }
                else if (result.Error is not null)
                {
                    lastErrors[resultKey] = result.Error;
                }
            }
        }

        foreach (var key in pending)
        {
            if (results.ContainsKey(key))
                continue;

            var error = lastErrors.GetValueOrDefault(key) ?? Unsupported(key);
            if (TryBuildStaleFallback(key, error, now, out var staleResult))
            {
                results[key] = staleResult;
                continue;
            }

            results[key] = MarketDataResult<EquityQuote>.Failure(error);
        }

        return orderedKeys.Select(k => results[k]).ToList();
    }

    private static MarketDataError Unsupported(EquityInstrumentKey key) =>
        new(
            MarketDataErrorCode.UnsupportedSymbol,
            $"No quote provider can handle {key}.",
            Instrument: key);

    private static MarketDataError ProviderUnavailable(string provider, EquityInstrumentKey key) =>
        new(
            MarketDataErrorCode.ProviderUnavailable,
            $"{provider} quote provider failed.",
            Provider: provider,
            Instrument: key,
            IsRetryable: true);

    private bool TryBuildStaleFallback(
        EquityInstrumentKey key,
        MarketDataError error,
        DateTimeOffset now,
        out MarketDataResult<EquityQuote> result)
    {
        result = default!;
        if (_cache is null || !CanUseStaleQuote(error))
            return false;

        if (!_cache.TryGet(key, TimeSpan.MaxValue, now, out var staleQuote))
            return false;

        result = MarketDataResult<EquityQuote>.Success(staleQuote with
        {
            IsStale = true,
            ProviderStateMessage = error.Message,
        });
        return true;
    }

    private static bool CanUseStaleQuote(MarketDataError error)
    {
        if (error.Code is MarketDataErrorCode.MissingApiKey
            or MarketDataErrorCode.UnsupportedSymbol
            or MarketDataErrorCode.CalendarClosed)
        {
            return false;
        }

        return error.IsRetryable
            || error.IsQuotaRelated
            || error.Code is MarketDataErrorCode.ProviderUnavailable
                or MarketDataErrorCode.NetworkFailure
                or MarketDataErrorCode.InvalidResponse
                or MarketDataErrorCode.Unknown;
    }
}
