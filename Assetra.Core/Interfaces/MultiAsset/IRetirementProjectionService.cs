using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRetirementProjectionService
{
    /// <summary>
    /// 所有活躍退休專戶的當前餘額總和。
    /// </summary>
    Task<decimal> GetTotalBalanceAsync(CancellationToken ct = default);

    /// <summary>
    /// 對單一帳戶以複利模型推算指定退休年齡時的預期餘額。
    /// 使用者輸入目前年齡與年化報酬率假設。
    /// </summary>
    Task<RetirementProjection?> ProjectAsync(
        Guid accountId,
        int currentAge,
        decimal annualReturnRate,
        decimal annualContribution,
        CancellationToken ct = default);

    /// <summary>
    /// 取得每個活躍帳戶的摘要與年度提撥總額（最近一年）。
    /// </summary>
    Task<IReadOnlyList<RetirementAccountSummary>> GetAccountSummariesAsync(CancellationToken ct = default);
}

public sealed record RetirementProjection(
    Guid AccountId,
    decimal CurrentBalance,
    int YearsToWithdrawal,
    decimal ProjectedBalance,
    decimal TotalContributions);

public sealed record RetirementAccountSummary(
    RetirementAccount Account,
    decimal LatestYearContribution);
