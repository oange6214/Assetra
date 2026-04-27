using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;

namespace Assetra.Application.Reconciliation;

/// <summary>
/// 對帳作業協調服務。
/// 比對演算法：
/// 1. 篩選帳戶 + 期間 trades
/// 2. 以 <see cref="IReconciliationMatcher"/> 雙向配對 statementRows × trades
/// 3. 配對成功且金額完全一致 → 不產 diff；金額在 abs 容忍度內但有差異 → AmountMismatch
/// 4. 對帳單未配對 → Missing；trade 未配對 → Extra
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly IReconciliationSessionRepository _sessions;
    private readonly ITradeRepository _trades;
    private readonly IReconciliationMatcher _matcher;
    private readonly TimeProvider _clock;

    public ReconciliationService(
        IReconciliationSessionRepository sessions,
        ITradeRepository trades,
        IReconciliationMatcher matcher,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(matcher);
        _sessions = sessions;
        _trades = trades;
        _matcher = matcher;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ReconciliationSession> CreateAsync(
        Guid accountId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<ImportPreviewRow> statementRows,
        Guid? sourceBatchId,
        string? note,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(statementRows);
        if (periodEnd < periodStart)
            throw new ArgumentException("PeriodEnd must be >= PeriodStart.", nameof(periodEnd));

        var session = new ReconciliationSession(
            Id: Guid.NewGuid(),
            AccountId: accountId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            SourceBatchId: sourceBatchId,
            CreatedAt: _clock.GetUtcNow(),
            Status: ReconciliationStatus.Open,
            Note: note);

        var trades = await LoadTradesAsync(accountId, periodStart, periodEnd, ct).ConfigureAwait(false);
        var diffs = ComputeDiffs(session.Id, statementRows, trades);
        await _sessions.AddAsync(session, statementRows, diffs, ct).ConfigureAwait(false);
        return session;
    }

    public async Task<IReadOnlyList<ReconciliationDiff>> RecomputeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");
        if (session.Status != ReconciliationStatus.Open)
            throw new InvalidOperationException("Only Open sessions can be recomputed.");

        var rows = await _sessions.GetStatementRowsAsync(sessionId, ct).ConfigureAwait(false);
        var trades = await LoadTradesAsync(session.AccountId, session.PeriodStart, session.PeriodEnd, ct).ConfigureAwait(false);
        var diffs = ComputeDiffs(session.Id, rows, trades);
        await _sessions.ReplaceDiffsAsync(session.Id, diffs, ct).ConfigureAwait(false);
        return diffs;
    }

    public async Task ApplyResolutionAsync(
        Guid diffId,
        ReconciliationDiffResolution resolution,
        string? note,
        CancellationToken ct = default)
    {
        var diff = await FindDiffAsync(diffId, ct).ConfigureAwait(false);
        EnsureLegalTransition(diff.Kind, resolution);

        switch (resolution)
        {
            case ReconciliationDiffResolution.Deleted when diff.TradeId is { } tid:
                await _trades.RemoveAsync(tid).ConfigureAwait(false);
                break;
            // Created / OverwrittenFromStatement 涉及 trade 物件構造，由上層呼叫者
            // 透過 ImportRowMapper 完成 trade 寫入後再呼叫此方法標記狀態。
        }

        await _sessions.UpdateDiffResolutionAsync(
            diffId, resolution, _clock.GetUtcNow(), note, ct).ConfigureAwait(false);
    }

    public async Task SignOffAsync(Guid sessionId, string? note, CancellationToken ct = default)
    {
        var diffs = await _sessions.GetDiffsAsync(sessionId, ct).ConfigureAwait(false);
        if (diffs.Any(d => d.Resolution == ReconciliationDiffResolution.Pending))
            throw new InvalidOperationException("Cannot sign off while pending diffs remain.");

        await _sessions.UpdateStatusAsync(sessionId, ReconciliationStatus.Resolved, note, ct).ConfigureAwait(false);
    }

    private async Task<ReconciliationDiff> FindDiffAsync(Guid diffId, CancellationToken ct)
    {
        // No direct GetById; iterate sessions until found. Cost is bounded by Open sessions.
        var sessions = await _sessions.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var s in sessions)
        {
            var diffs = await _sessions.GetDiffsAsync(s.Id, ct).ConfigureAwait(false);
            var match = diffs.FirstOrDefault(d => d.Id == diffId);
            if (match is not null) return match;
        }
        throw new InvalidOperationException($"Diff {diffId} not found.");
    }

    private async Task<IReadOnlyList<Trade>> LoadTradesAsync(
        Guid accountId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var all = await _trades.GetByCashAccountAsync(accountId).ConfigureAwait(false);
        var startDt = periodStart.ToDateTime(TimeOnly.MinValue);
        var endDt = periodEnd.ToDateTime(TimeOnly.MaxValue);
        return all.Where(t => t.TradeDate >= startDt && t.TradeDate <= endDt).ToList();
    }

    public IReadOnlyList<ReconciliationDiff> ComputeDiffs(
        Guid sessionId,
        IReadOnlyList<ImportPreviewRow> statementRows,
        IReadOnlyList<Trade> trades) => ComputeDiffs(sessionId, statementRows, trades, _matcher);

    public static IReadOnlyList<ReconciliationDiff> ComputeDiffs(
        Guid sessionId,
        IReadOnlyList<ImportPreviewRow> statementRows,
        IReadOnlyList<Trade> trades,
        IReconciliationMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(statementRows);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(matcher);

        var diffs = new List<ReconciliationDiff>();
        var matchedTradeIdx = new HashSet<int>();

        for (int ri = 0; ri < statementRows.Count; ri++)
        {
            var row = statementRows[ri];
            int? bestIdx = null;
            for (int ti = 0; ti < trades.Count; ti++)
            {
                if (matchedTradeIdx.Contains(ti)) continue;
                if (matcher.IsMatch(row, trades[ti]))
                {
                    bestIdx = ti;
                    break;
                }
            }

            if (bestIdx is null)
            {
                diffs.Add(NewDiff(sessionId, ReconciliationDiffKind.Missing, row, trade: null));
                continue;
            }

            matchedTradeIdx.Add(bestIdx.Value);
            var trade = trades[bestIdx.Value];
            var rowAmt = matcher.SignedAmount(row);
            var tradeAmt = matcher.SignedAmount(trade);
            if (rowAmt != tradeAmt)
            {
                diffs.Add(NewDiff(sessionId, ReconciliationDiffKind.AmountMismatch, row, trade));
            }
        }

        for (int ti = 0; ti < trades.Count; ti++)
        {
            if (matchedTradeIdx.Contains(ti)) continue;
            diffs.Add(NewDiff(sessionId, ReconciliationDiffKind.Extra, statementRow: null, trade: trades[ti]));
        }

        return diffs;
    }

    private static ReconciliationDiff NewDiff(
        Guid sessionId, ReconciliationDiffKind kind, ImportPreviewRow? statementRow, Trade? trade)
        => new(
            Id: Guid.NewGuid(),
            SessionId: sessionId,
            Kind: kind,
            StatementRow: statementRow,
            TradeId: trade?.Id,
            Resolution: ReconciliationDiffResolution.Pending,
            ResolvedAt: null,
            Note: null);

    /// <summary>
    /// Kind × Resolution 合法表：
    /// Missing       → Created / MarkedResolved / Ignored
    /// Extra         → Deleted / MarkedResolved / Ignored
    /// AmountMismatch → OverwrittenFromStatement / MarkedResolved / Ignored
    /// </summary>
    public static void EnsureLegalTransition(ReconciliationDiffKind kind, ReconciliationDiffResolution resolution)
    {
        if (resolution == ReconciliationDiffResolution.Pending)
            throw new InvalidOperationException("Pending is not a target resolution.");

        var legal = (kind, resolution) switch
        {
            (ReconciliationDiffKind.Missing, ReconciliationDiffResolution.Created) => true,
            (ReconciliationDiffKind.Extra, ReconciliationDiffResolution.Deleted) => true,
            (ReconciliationDiffKind.AmountMismatch, ReconciliationDiffResolution.OverwrittenFromStatement) => true,
            (_, ReconciliationDiffResolution.MarkedResolved) => true,
            (_, ReconciliationDiffResolution.Ignored) => true,
            _ => false,
        };
        if (!legal)
            throw new InvalidOperationException($"Resolution {resolution} is not legal for kind {kind}.");
    }
}
