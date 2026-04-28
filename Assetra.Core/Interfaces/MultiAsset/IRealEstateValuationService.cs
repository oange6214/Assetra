using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRealEstateValuationService
{
    /// <summary>
    /// 計算所有活躍不動產的總市值（扣除房貸後為 Equity）。
    /// </summary>
    Task<decimal> GetTotalCurrentValueAsync(CancellationToken ct = default);

    /// <summary>
    /// 計算所有活躍不動產的總淨值（CurrentValue − MortgageBalance）。
    /// </summary>
    Task<decimal> GetTotalEquityAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得所有活躍不動產，並附上最新一期租金收入摘要。
    /// </summary>
    Task<IReadOnlyList<RealEstateValuationSummary>> GetValuationSummariesAsync(CancellationToken ct = default);
}

public sealed record RealEstateValuationSummary(
    RealEstate Property,
    decimal MonthlyNetRental);
