namespace Assetra.Core.Models;

/// <summary>「建立資金帳戶」的結果狀態。</summary>
public enum AccountCreateStatus
{
    /// <summary>全新建立。</summary>
    Created,

    /// <summary>
    /// 原本已封存或已軟刪除的同名同幣別帳戶被「就地復活」成全新啟用帳戶
    /// （沿用既有列的 Id，套用本次建立的設定）。
    /// </summary>
    Revived,

    /// <summary>已存在一個「啟用中」的同名同幣別帳戶，未做任何更動。</summary>
    DuplicateActive,
}

/// <summary>
/// <see cref="Interfaces.IAssetRepository.CreateOrReviveAccountAsync"/> 的結果。
/// </summary>
/// <param name="Id">實際使用的帳戶 Id；復活時為既有列的 Id，而非傳入的新 Id。</param>
/// <param name="Status">建立 / 復活 / 重複。</param>
public readonly record struct AccountCreateOutcome(Guid Id, AccountCreateStatus Status);
