using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Fx;

/// <summary>
/// Converts a list of <see cref="CashFlow"/> tagged with source currency into a single base
/// currency. Flows with null/empty <see cref="CashFlow.Currency"/> or matching the base currency
/// pass through unchanged. Missing FX rates cause the whole conversion to abort with null —
/// callers (XIRR / MWR) treat that as "cannot compute" rather than silently dropping flows.
/// </summary>
public static class MultiCurrencyCashFlowConverter
{
    public static async Task<IReadOnlyList<CashFlow>?> ConvertAllAsync(
        IReadOnlyList<CashFlow> flows,
        string baseCurrency,
        IMultiCurrencyValuationService fx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(fx);
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency required.", nameof(baseCurrency));

        var result = new List<CashFlow>(flows.Count);
        foreach (var f in flows)
        {
            if (string.IsNullOrWhiteSpace(f.Currency)
                || string.Equals(f.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(f with { Currency = baseCurrency });
                continue;
            }

            var converted = await fx.ConvertAsync(f.Amount, f.Currency, baseCurrency, f.Date, ct).ConfigureAwait(false);
            if (converted is null) return null;

            result.Add(f with { Amount = converted.Value, Currency = baseCurrency });
        }
        return result;
    }
}
