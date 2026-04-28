using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;

namespace Assetra.Application.Fx;

/// <summary>
/// 走 <see cref="IFxRateProvider"/> 換算金額。同幣別捷徑回 amount，找不到匯率回 null。
/// </summary>
public sealed class MultiCurrencyValuationService : IMultiCurrencyValuationService
{
    private readonly IFxRateProvider _fx;

    public MultiCurrencyValuationService(IFxRateProvider fx)
    {
        _fx = fx ?? throw new ArgumentNullException(nameof(fx));
    }

    public async Task<decimal?> ConvertAsync(
        decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return null;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return amount;

        var rate = await _fx.GetRateAsync(from, to, asOf, ct).ConfigureAwait(false);
        return rate is null ? null : amount * rate.Value;
    }
}
