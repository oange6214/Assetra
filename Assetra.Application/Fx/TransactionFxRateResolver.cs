using Assetra.Core.Interfaces;

namespace Assetra.Application.Fx;

public sealed class TransactionFxRateResolver
{
    private const string SameCurrencySource = "same-currency";

    private readonly IFxRateHistoryService _history;

    public TransactionFxRateResolver(IFxRateHistoryService history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public async Task<TransactionFxQuote> ResolveAsync(
        DateOnly tradeDate,
        string instrumentCurrency,
        string settlementCurrency,
        CancellationToken ct = default)
    {
        var from = NormalizeCurrency(instrumentCurrency);
        var to = NormalizeCurrency(settlementCurrency);

        if (from.Length == 0 || to.Length == 0)
        {
            return new TransactionFxQuote(
                from,
                to,
                Rate: null,
                RateDate: null,
                Source: null,
                IsEstimated: false,
                Status: TransactionFxQuoteStatus.MissingInput);
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new TransactionFxQuote(
                from,
                to,
                Rate: 1m,
                RateDate: tradeDate,
                Source: SameCurrencySource,
                IsEstimated: false,
                Status: TransactionFxQuoteStatus.SameCurrency);
        }

        var entry = await _history.GetEntryAsync(tradeDate, from, to, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return new TransactionFxQuote(
                from,
                to,
                Rate: null,
                RateDate: null,
                Source: null,
                IsEstimated: false,
                Status: TransactionFxQuoteStatus.Unavailable);
        }

        return new TransactionFxQuote(
            from,
            to,
            Rate: entry.Rate,
            RateDate: entry.Date,
            Source: entry.Source,
            IsEstimated: entry.Date != tradeDate,
            Status: TransactionFxQuoteStatus.Resolved);
    }

    private static string NormalizeCurrency(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}
