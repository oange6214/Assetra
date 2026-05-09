namespace Assetra.Core.Interfaces;

/// <summary>
/// AI 助手 Phase 2.5 — 對話記錄持久化。每個 (user message + assistant response)
/// pair 為一筆 entry，跨 session 可回顧。
///
/// <para>
/// 隱私：使用者輸入文字 + 助手回答純文字（不含結構化財務 snapshot）。
/// 使用者可隨時呼叫 <see cref="ClearAsync"/> 清空。
/// </para>
/// </summary>
public interface IAssistantHistoryRepository
{
    /// <summary>取最近 <paramref name="limit"/> 筆對話，依時間降冪排序。</summary>
    Task<IReadOnlyList<AssistantHistoryEntry>> GetRecentAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>新增一筆對話記錄。</summary>
    Task AddAsync(AssistantHistoryEntry entry, CancellationToken ct = default);

    /// <summary>清空所有對話記錄。</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>單筆對話記錄（一個問題 + 一個回答 + metadata）。</summary>
/// <param name="Id">unique row id（SQLite autoincrement 或 Guid，依實作）。</param>
/// <param name="AskedAt">使用者送出查詢的 UTC 時刻。</param>
/// <param name="UserText">使用者輸入。</param>
/// <param name="AssistantText">助手回答主文。</param>
/// <param name="Source">回答來源（"BalanceQueryService" / "openai/gpt-4o-mini" 等）。</param>
public sealed record AssistantHistoryEntry(
    Guid Id,
    DateTime AskedAt,
    string UserText,
    string AssistantText,
    string Source);
