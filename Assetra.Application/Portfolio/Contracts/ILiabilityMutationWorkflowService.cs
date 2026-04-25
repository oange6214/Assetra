namespace Assetra.Application.Portfolio.Contracts;

public interface ILiabilityMutationWorkflowService
{
    /// <summary>
    /// 永久刪除負債（貸款 / 信用卡）。連帶移除所有引用此負債的交易記錄
    /// （信用卡：liability_asset_id；貸款：loan_label）。
    /// </summary>
    Task<LiabilityDeletionResult> DeleteAsync(LiabilityDeletionRequest request, CancellationToken ct = default);
}

public sealed record LiabilityDeletionRequest(Guid? AssetId, string? LoanLabel);

public sealed record LiabilityDeletionResult(bool Success);
