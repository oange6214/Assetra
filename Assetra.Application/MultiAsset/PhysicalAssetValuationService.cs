using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 實物資產估值服務：彙整活躍實物資產的當前市值與未實現損益。
/// </summary>
public sealed class PhysicalAssetValuationService : IPhysicalAssetValuationService
{
    private readonly IPhysicalAssetRepository _assets;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public PhysicalAssetValuationService(
        IPhysicalAssetRepository assets,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
        _fx = fx;
        _settings = settings;
    }

    public async Task<decimal> GetTotalCurrentValueAsync(CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        var total = 0m;
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        foreach (var asset in all.Where(a => a.Status == PhysicalAssetStatus.Active))
            total += await ConvertToBaseOrOriginalAsync(asset.CurrentValue, asset.Currency, asOf, ct).ConfigureAwait(false);
        return total;
    }

    public async Task<decimal> GetTotalUnrealizedGainAsync(CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        var total = 0m;
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        foreach (var asset in all.Where(a => a.Status == PhysicalAssetStatus.Active))
            total += await ConvertToBaseOrOriginalAsync(asset.UnrealizedGain, asset.Currency, asOf, ct).ConfigureAwait(false);
        return total;
    }

    public async Task<IReadOnlyList<PhysicalAssetSummary>> GetSummariesAsync(
        CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        var active = all.Where(a => a.Status == PhysicalAssetStatus.Active).ToList();

        var results = new List<PhysicalAssetSummary>(active.Count);
        foreach (var asset in active)
        {
            ct.ThrowIfCancellationRequested();
            var rate = asset.AcquisitionCost == 0m
                ? 0m
                : asset.UnrealizedGain / asset.AcquisitionCost;
            results.Add(new PhysicalAssetSummary(asset, asset.UnrealizedGain, rate));
        }
        return results;
    }

    private string? GetBaseCurrency() => _settings?.Current.BaseCurrency;

    private async Task<decimal> ConvertToBaseOrOriginalAsync(
        decimal amount,
        string fromCurrency,
        DateOnly asOf,
        CancellationToken ct)
    {
        var baseCurrency = GetBaseCurrency();
        if (_fx is null
            || string.IsNullOrWhiteSpace(baseCurrency)
            || string.IsNullOrWhiteSpace(fromCurrency)
            || string.Equals(fromCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        var converted = await _fx.ConvertAsync(amount, fromCurrency, baseCurrency, asOf, ct).ConfigureAwait(false);
        return converted ?? amount;
    }
}
