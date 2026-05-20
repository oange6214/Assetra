namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// AddRecordDialog Phase 2 — 統一的「資產選擇器」item，把 PortfolioEntry / CashAccount /
/// Liability 三類資產收成同一個下拉清單的 row。
/// </summary>
public sealed record TxAssetSubject(
    TxAssetKind Kind,
    System.Guid Id,
    /// <summary>第一行顯示：「NVIDIA Corp」/「USD Savings」/「台新 7y B」。</summary>
    string PrimaryName,
    /// <summary>第二行（小字）：「(NVDA) · USD」/「CASH · USD」/「DEBT · TWD」。</summary>
    string SecondaryLine,
    /// <summary>分組 header DynamicResource key — 「投資」/「現金」/「負債」。</summary>
    string GroupKey,
    string Currency,
    string? Symbol = null,
    System.Guid? SuggestedCashAccountId = null)
{
    /// <summary>
    /// 向後相容：早期 callsite 直接綁 Display 拿到合併字串。
    /// 新 XAML 用 ItemTemplate 直接綁 PrimaryName / SecondaryLine 分行渲染。
    /// </summary>
    public string Display => string.IsNullOrEmpty(SecondaryLine)
        ? PrimaryName
        : $"{PrimaryName} · {SecondaryLine}";
}

public enum TxAssetKind
{
    None,
    Stock,
    Fund,
    Crypto,
    Metal,
    Bond,
    CashAccount,
    Liability,
}
