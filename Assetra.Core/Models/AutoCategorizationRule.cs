namespace Assetra.Core.Models;

/// <summary>規則比對的文字來源。</summary>
public enum AutoCategorizationMatchField
{
    /// <summary>對方/商家欄（匯入時 ImportPreviewRow.Counterparty）。</summary>
    Counterparty = 0,
    /// <summary>備註欄（匯入時 ImportPreviewRow.Memo）。</summary>
    Memo = 1,
    /// <summary>對方或備註任一命中即視為命中（匯入時兩欄都檢查）。</summary>
    Either = 2,
    /// <summary>完整內文（手動：Trade.Note；匯入：Counterparty " / " Memo 串接）— 預設值，對應 v0.7 行為。</summary>
    AnyText = 3,
}

/// <summary>規則比對方式。</summary>
public enum AutoCategorizationMatchType
{
    /// <summary>子字串包含（預設）。</summary>
    Contains = 0,
    /// <summary>完全相等。</summary>
    Equals = 1,
    /// <summary>字首相符。</summary>
    StartsWith = 2,
    /// <summary>正規表達式（編譯失敗的規則會被 engine 略過）。</summary>
    Regex = 3,
}

/// <summary>規則套用範圍（旗標型）。</summary>
[Flags]
public enum AutoCategorizationScope
{
    None = 0,
    /// <summary>手動建立交易時（TransactionDialog Note 欄輸入時）。</summary>
    Manual = 1,
    /// <summary>匯入 CSV/Excel 對帳單時。</summary>
    Import = 2,
    /// <summary>同時套用於手動與匯入（預設）。</summary>
    Both = Manual | Import,
}

/// <summary>
/// 自動分類規則。<br/>
/// v0.7 起：以 <see cref="KeywordPattern"/> 對 <c>Trade.Note</c> 做包含比對（仍保留為 Layer 1 的簡單模式）。<br/>
/// v0.8 起：擴增 <see cref="MatchField"/> / <see cref="MatchType"/> / <see cref="AppliesTo"/>，
/// 同時支援匯入時對 Counterparty / Memo 精準比對與正規表達式。<br/>
/// 新欄位皆有預設值對應 v0.7 行為，以維持既有資料與呼叫者向後相容。<br/>
/// Priority 越小越優先；同筆內容僅套用第一條符合的啟用規則。
/// </summary>
public sealed record AutoCategorizationRule(
    Guid Id,
    string KeywordPattern,
    Guid CategoryId,
    int Priority = 0,
    bool IsEnabled = true,
    bool MatchCaseSensitive = false,
    string? Name = null,
    AutoCategorizationMatchField MatchField = AutoCategorizationMatchField.AnyText,
    AutoCategorizationMatchType MatchType = AutoCategorizationMatchType.Contains,
    AutoCategorizationScope AppliesTo = AutoCategorizationScope.Both)
{
    /// <summary>UI 顯示名：若 <see cref="Name"/> 為空則 fallback 至 <see cref="KeywordPattern"/>。</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? KeywordPattern : Name!;
}

/// <summary>
/// 規則比對上下文。<br/>
/// 手動模式：填 <see cref="Note"/>（Source = Manual）。<br/>
/// 匯入模式：填 <see cref="Counterparty"/> 與 <see cref="Memo"/>（Source = Import），
/// engine 會視 <see cref="AutoCategorizationRule.MatchField"/> 自動取對應欄位或合併。
/// </summary>
public sealed record AutoCategorizationContext(
    string? Note,
    string? Counterparty,
    string? Memo,
    AutoCategorizationScope Source);
