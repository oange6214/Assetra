using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 不動產估值服務：彙整所有活躍不動產的市值、淨值與租金收入摘要。
/// </summary>
public sealed class RealEstateValuationService : IRealEstateValuationService
{
    private readonly IRealEstateRepository _properties;
    private readonly IRentalIncomeRecordRepository _rentalRecords;

    public RealEstateValuationService(
        IRealEstateRepository properties,
        IRentalIncomeRecordRepository rentalRecords)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(rentalRecords);
        _properties = properties;
        _rentalRecords = rentalRecords;
    }

    public async Task<decimal> GetTotalCurrentValueAsync(CancellationToken ct = default)
    {
        var all = await _properties.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(p => p.Status == RealEstateStatus.Active).Sum(p => p.CurrentValue);
    }

    public async Task<decimal> GetTotalEquityAsync(CancellationToken ct = default)
    {
        var all = await _properties.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(p => p.Status == RealEstateStatus.Active).Sum(p => p.Equity);
    }

    public async Task<IReadOnlyList<RealEstateValuationSummary>> GetValuationSummariesAsync(
        CancellationToken ct = default)
    {
        var all = await _properties.GetAllAsync(ct).ConfigureAwait(false);
        var active = all.Where(p => p.Status == RealEstateStatus.Active).ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var results = new List<RealEstateValuationSummary>(active.Count);
        foreach (var prop in active)
        {
            ct.ThrowIfCancellationRequested();
            decimal monthlyNet = 0m;
            if (prop.IsRental)
            {
                var records = await _rentalRecords
                    .GetByPropertyAsync(prop.Id, ct).ConfigureAwait(false);
                // Use the most recent month's net income as the representative figure
                var latest = records
                    .Where(r => r.Month >= monthStart.AddMonths(-11))
                    .OrderByDescending(r => r.Month)
                    .FirstOrDefault();
                monthlyNet = latest?.NetIncome ?? 0m;
            }
            results.Add(new RealEstateValuationSummary(prop, monthlyNet));
        }
        return results;
    }
}
