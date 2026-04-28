using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IInsuranceCashValueCalculator
{
    /// <summary>
    /// 計算所有有效保單的總現金價值。
    /// </summary>
    Task<decimal> GetTotalCashValueAsync(CancellationToken ct = default);

    /// <summary>
    /// 計算所有有效保單的年繳保費總額。
    /// </summary>
    Task<decimal> GetTotalAnnualPremiumAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得每張有效保單的現金價值摘要。
    /// </summary>
    Task<IReadOnlyList<InsuranceCashValueSummary>> GetCashValueSummariesAsync(CancellationToken ct = default);
}

public sealed record InsuranceCashValueSummary(
    InsurancePolicy Policy,
    decimal CashValue,
    decimal TotalPremiumsPaid);
