namespace Assetra.Application.Portfolio.Contracts;

public interface ILiabilityMutationWorkflowService
{
    /// <summary>
    /// 永久刪除負債（貸款 / 信用卡）。連帶移除所有引用此負債的交易記錄
    /// （信用卡：liability_asset_id；貸款：loan_label）。
    /// </summary>
    Task<LiabilityDeletionResult> DeleteAsync(LiabilityDeletionRequest request, CancellationToken ct = default);

    /// <summary>
    /// 編輯既有負債的中繼資料。Loan 可改利率/期數/手續費/Issuer/Subtype；
    /// 信用卡可改額度/帳單日/繳款日/Issuer/Subtype。
    /// 名稱（<see cref="LiabilityUpdateRequest.NewName"/>）跨類型可改。
    ///
    /// <para>
    /// 對 Loan：當 <see cref="LiabilityUpdateRequest.NewAnnualRate"/> 或
    /// <see cref="LiabilityUpdateRequest.NewTermMonths"/> 與當前資產不同，
    /// 並且 <see cref="LiabilityUpdateRequest.RecomputeSchedule"/> = <c>true</c>，
    /// 服務會呼叫 <c>ILoanScheduleRecomputeService</c> 把未付期重新生成
    /// （已付期保留原樣不動）。已付期的 PrincipalAmount 從 OriginalPrincipal
    /// 扣掉後攤到剩餘期數。
    /// </para>
    ///
    /// <para>
    /// Label / loan_label 永遠不變更（這是 trade FK 的 join key），改名只動
    /// AssetItem.Name（顯示名稱）。
    /// </para>
    /// </summary>
    Task<LiabilityUpdateResult> UpdateAsync(LiabilityUpdateRequest request, CancellationToken ct = default);
}

public sealed record LiabilityDeletionRequest(Guid? AssetId, string? LoanLabel);

public sealed record LiabilityDeletionResult(bool Success);

/// <summary>
/// All "New*" fields are nullable. <c>null</c> = "do not change this field"
/// (the workflow re-uses the existing AssetItem value). For Loan recompute
/// to fire, both <see cref="NewAnnualRate"/>/<see cref="NewTermMonths"/>
/// AND <see cref="RecomputeSchedule"/>=<c>true</c> AND
/// <see cref="OriginalPrincipal"/> must be supplied.
/// </summary>
public sealed record LiabilityUpdateRequest(
    Guid AssetId,
    string? NewName = null,
    string? NewIssuerName = null,
    string? NewSubtype = null,
    // Loan-only
    decimal? NewAnnualRate = null,
    int? NewTermMonths = null,
    decimal? NewHandlingFee = null,
    bool RecomputeSchedule = false,
    decimal? OriginalPrincipal = null,
    // CreditCard-only
    decimal? NewCreditLimit = null,
    int? NewBillingDay = null,
    int? NewDueDay = null);

public sealed record LiabilityUpdateResult(
    bool Success,
    bool ScheduleRecomputed,
    int PreservedPaidCount = 0,
    int RegeneratedUnpaidCount = 0);
