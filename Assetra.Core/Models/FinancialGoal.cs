namespace Assetra.Core.Models;

/// <summary>
/// 財務目標：使用者自訂的儲蓄／淨資產目標。
///
/// <para>
/// 進度 (ProgressPercent) 來源有兩種模式：
/// <list type="number">
///   <item><b>Manual</b>：<see cref="LinkedAssetClass"/> 為 null 或空字串時，
///         進度 = <see cref="CurrentAmount"/> / <see cref="TargetAmount"/>。
///         使用者要手動回來編輯 CurrentAmount 才會更新。</item>
///   <item><b>Auto</b>：<see cref="LinkedAssetClass"/> 設為支援的類別字串時，
///         caller (e.g. <c>FinancialOverviewViewModel.PrimaryGoal</c>) 會用該類別
///         在 dashboard 上的當前資產淨值直接算進度，<see cref="CurrentAmount"/>
///         被忽略。每次交易 / 報價更新後進度自動同步真實資產。</item>
/// </list>
/// 過渡方案 — Portfolio-Groups-Refactor 完成後會升級為 <c>PortfolioId</c> 連結具體
/// 群組（含使用者自訂的「買房」「退休」buckets），<see cref="LinkedAssetClass"/>
/// 屆時會 migration 成對應 default group。
/// </para>
///
/// <para><b>LinkedAssetClass 支援的值</b>（caller 應對齊）：</para>
/// <list type="bullet">
///   <item><c>"NetWorth"</c>     — 整體淨資產（資產 − 負債）</item>
///   <item><c>"TotalAssets"</c>  — 總資產（不扣負債）</item>
///   <item><c>"Investments"</c>  — 投資資產市值</item>
///   <item><c>"Cash"</c>         — 現金帳戶總和</item>
///   <item><c>"RealEstate"</c>   — 不動產總值</item>
///   <item><c>"Retirement"</c>   — 退休帳戶總值</item>
///   <item><c>"Physical"</c>     — 實物資產總值</item>
///   <item><c>null</c> / 空字串  — Manual mode（沿用 <see cref="CurrentAmount"/>）</item>
/// </list>
/// </summary>
public sealed record FinancialGoal(
    Guid Id,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateOnly? Deadline,
    string? Notes,
    /// <summary>
    /// Auto-tracking link — 設為支援的類別字串（見 type-level doc）時，goal 進度
    /// 跟著該類別的當前 dashboard 值動態變化，<see cref="CurrentAmount"/> 被視為
    /// 「初始輸入值」但顯示時被覆蓋。null = manual mode（沿用既有行為）。
    /// </summary>
    string? LinkedAssetClass = null,
    /// <summary>
    /// Portfolio-Groups-Refactor P5 — 連結到具體投資組合群組（bucket）。
    /// 設定後優先於 <see cref="LinkedAssetClass"/>：caller 用該群組的當前淨值算進度，
    /// 給「買房儲蓄」「退休帳戶」這種使用者自訂 bucket 更精準的追蹤。
    /// null = 不連結（沿用 LinkedAssetClass / 手動模式）。
    /// </summary>
    Guid? PortfolioGroupId = null)
{
    /// <summary>True 當此 goal 啟用 auto-tracking（非 manual）。</summary>
    public bool IsAutoTracked => !string.IsNullOrWhiteSpace(LinkedAssetClass);

    /// <summary>
    /// Manual mode 的進度（≤ 100%）。Auto mode 時 caller 應用 dashboard 值算，
    /// 不要呼叫此 property。
    /// </summary>
    public decimal ProgressPercent =>
        TargetAmount > 0
            ? Math.Min(CurrentAmount / TargetAmount * 100m, 100m)
            : 0m;

    public bool IsAchieved => TargetAmount > 0 && CurrentAmount >= TargetAmount;

    public decimal Remaining =>
        Math.Max(TargetAmount - CurrentAmount, 0m);

    public int? DaysRemaining =>
        Deadline is { } d ? (d.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber) : null;
}
