using Assetra.Application.Reconciliation;
using Assetra.Core.DomainServices.Reconciliation;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;
using Xunit;

namespace Assetra.Tests.Application.Reconciliation;

/// <summary>
/// v0.10.1 補測：v0.10.0 新增 <see cref="IReconciliationService.ApplyResolutionAsync"/> 的 sourceKind/options overload，
/// 含 Created / OverwrittenFromStatement 兩條會動到 trade 的執行路徑，當時未補上單元測試。
/// 此檔案以 hand-rolled fakes 直驅服務層，覆蓋五條合法 resolution + 三條防呆。
/// </summary>
public class ApplyResolutionAsyncTests
{
    private static ImportPreviewRow Row(decimal amount, DateOnly? date = null) => new(
        RowIndex: 1,
        Date: date ?? new DateOnly(2026, 4, 28),
        Amount: amount,
        Counterparty: "ACME",
        Memo: null);

    private static Trade NewTrade(Guid id, decimal cashAmount) => new(
        Id: id,
        Symbol: string.Empty,
        Exchange: string.Empty,
        Name: string.Empty,
        Type: TradeType.Income,
        TradeDate: new DateTime(2026, 4, 28),
        Price: 0m,
        Quantity: 1,
        RealizedPnl: null,
        RealizedPnlPct: null,
        CashAmount: cashAmount);

    private static ReconciliationDiff Diff(
        ReconciliationDiffKind kind, ImportPreviewRow? row = null, Guid? tradeId = null) => new(
        Id: Guid.NewGuid(),
        SessionId: Guid.NewGuid(),
        Kind: kind,
        StatementRow: row,
        TradeId: tradeId,
        Resolution: ReconciliationDiffResolution.Pending,
        ResolvedAt: null,
        Note: null);

    private static (ReconciliationService svc, FakeSessions sessions, FakeTrades trades, FakeApplier applier)
        BuildServiceWithDiff(ReconciliationDiff diff, Trade? seedTrade = null)
    {
        var sessions = new FakeSessions();
        sessions.Diffs[diff.Id] = diff;
        var trades = new FakeTrades();
        if (seedTrade is not null) trades.Store[seedTrade.Id] = seedTrade;
        var applier = new FakeApplier();
        var svc = new ReconciliationService(sessions, trades, new DefaultReconciliationMatcher(), applier);
        return (svc, sessions, trades, applier);
    }

    [Fact]
    public async Task Created_AppliesRowAsTrade_AndUpdatesDiff()
    {
        var row = Row(1500m);
        var diff = Diff(ReconciliationDiffKind.Missing, row: row);
        var (svc, sessions, _, applier) = BuildServiceWithDiff(diff);
        applier.NextResultId = Guid.NewGuid();

        await svc.ApplyResolutionAsync(
            diff.Id, ReconciliationDiffResolution.Created, note: "ok",
            ImportSourceKind.BankStatement,
            new ImportApplyOptions(CashAccountId: Guid.NewGuid()));

        Assert.Single(applier.Calls);
        Assert.Equal(row, applier.Calls[0].row);
        Assert.Equal(ImportSourceKind.BankStatement, applier.Calls[0].sourceKind);
        Assert.Equal((diff.Id, ReconciliationDiffResolution.Created, "ok"),
            (sessions.LastResolutionUpdate!.Value.diffId,
             sessions.LastResolutionUpdate!.Value.resolution,
             sessions.LastResolutionUpdate!.Value.note));
    }

    [Fact]
    public async Task Created_WithoutApplier_Throws()
    {
        var row = Row(1500m);
        var diff = Diff(ReconciliationDiffKind.Missing, row: row);
        var sessions = new FakeSessions();
        sessions.Diffs[diff.Id] = diff;
        var svc = new ReconciliationService(sessions, new FakeTrades(), new DefaultReconciliationMatcher(), applier: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyResolutionAsync(
                diff.Id, ReconciliationDiffResolution.Created, note: null,
                ImportSourceKind.BankStatement,
                new ImportApplyOptions()));
    }

    [Fact]
    public async Task Created_WhenMapperReturnsNull_Throws()
    {
        var row = Row(1500m);
        var diff = Diff(ReconciliationDiffKind.Missing, row: row);
        var (svc, _, _, applier) = BuildServiceWithDiff(diff);
        applier.NextResultId = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyResolutionAsync(
                diff.Id, ReconciliationDiffResolution.Created, note: null,
                ImportSourceKind.BankStatement,
                new ImportApplyOptions()));
    }

    [Fact]
    public async Task Deleted_RemovesTrade_AndUpdatesDiff()
    {
        var tradeId = Guid.NewGuid();
        var diff = Diff(ReconciliationDiffKind.Extra, tradeId: tradeId);
        var (svc, sessions, trades, _) = BuildServiceWithDiff(diff, seedTrade: NewTrade(tradeId, 800m));

        await svc.ApplyResolutionAsync(
            diff.Id, ReconciliationDiffResolution.Deleted, note: null,
            sourceKind: null, options: null);

        Assert.Contains(tradeId, trades.Removed);
        Assert.Equal(ReconciliationDiffResolution.Deleted, sessions.LastResolutionUpdate!.Value.resolution);
    }

    [Fact]
    public async Task OverwrittenFromStatement_UpdatesTradeCashAmount()
    {
        var tradeId = Guid.NewGuid();
        var seed = NewTrade(tradeId, cashAmount: 1000m);
        var row = Row(1234.56m);
        var diff = Diff(ReconciliationDiffKind.AmountMismatch, row: row, tradeId: tradeId);
        var (svc, sessions, trades, _) = BuildServiceWithDiff(diff, seedTrade: seed);

        await svc.ApplyResolutionAsync(
            diff.Id, ReconciliationDiffResolution.OverwrittenFromStatement, note: null,
            sourceKind: null, options: null);

        var updated = Assert.Single(trades.Updated);
        Assert.Equal(tradeId, updated.Id);
        Assert.Equal(1234.56m, updated.CashAmount);
        Assert.Equal(ReconciliationDiffResolution.OverwrittenFromStatement,
            sessions.LastResolutionUpdate!.Value.resolution);
    }

    [Fact]
    public async Task OverwrittenFromStatement_WhenTradeMissing_Throws()
    {
        var row = Row(1234.56m);
        var diff = Diff(ReconciliationDiffKind.AmountMismatch, row: row, tradeId: Guid.NewGuid());
        var (svc, _, _, _) = BuildServiceWithDiff(diff); // no seed trade

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyResolutionAsync(
                diff.Id, ReconciliationDiffResolution.OverwrittenFromStatement, note: null,
                sourceKind: null, options: null));
    }

    [Fact]
    public async Task MarkedResolved_DoesNotTouchTrades_OnlyUpdatesDiff()
    {
        var tradeId = Guid.NewGuid();
        var diff = Diff(ReconciliationDiffKind.Extra, tradeId: tradeId);
        var (svc, sessions, trades, applier) = BuildServiceWithDiff(diff, seedTrade: NewTrade(tradeId, 100m));

        await svc.ApplyResolutionAsync(
            diff.Id, ReconciliationDiffResolution.MarkedResolved, note: "manual",
            sourceKind: null, options: null);

        Assert.Empty(trades.Removed);
        Assert.Empty(trades.Updated);
        Assert.Empty(applier.Calls);
        Assert.Equal(ReconciliationDiffResolution.MarkedResolved, sessions.LastResolutionUpdate!.Value.resolution);
    }

    [Fact]
    public async Task Ignored_DoesNotTouchTrades_OnlyUpdatesDiff()
    {
        var diff = Diff(ReconciliationDiffKind.Missing, row: Row(50m));
        var (svc, sessions, trades, applier) = BuildServiceWithDiff(diff);

        await svc.ApplyResolutionAsync(
            diff.Id, ReconciliationDiffResolution.Ignored, note: null,
            sourceKind: null, options: null);

        Assert.Empty(trades.Removed);
        Assert.Empty(trades.Updated);
        Assert.Empty(applier.Calls);
        Assert.Equal(ReconciliationDiffResolution.Ignored, sessions.LastResolutionUpdate!.Value.resolution);
    }

    [Fact]
    public async Task IllegalKindResolution_ThrowsBeforeAnySideEffect()
    {
        var diff = Diff(ReconciliationDiffKind.Extra, tradeId: Guid.NewGuid());
        var (svc, sessions, trades, applier) = BuildServiceWithDiff(diff);

        // Extra + Created is illegal per matrix.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyResolutionAsync(
                diff.Id, ReconciliationDiffResolution.Created, note: null,
                ImportSourceKind.BankStatement, new ImportApplyOptions()));

        Assert.Empty(trades.Removed);
        Assert.Empty(trades.Updated);
        Assert.Empty(applier.Calls);
        Assert.Null(sessions.LastResolutionUpdate);
    }

    [Fact]
    public async Task UnknownDiffId_Throws()
    {
        var sessions = new FakeSessions();
        var svc = new ReconciliationService(sessions, new FakeTrades(), new DefaultReconciliationMatcher(), new FakeApplier());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyResolutionAsync(
                Guid.NewGuid(), ReconciliationDiffResolution.Ignored, note: null,
                sourceKind: null, options: null));
    }

    // --- Fakes ---------------------------------------------------------------

    private sealed class FakeSessions : IReconciliationSessionRepository
    {
        public Dictionary<Guid, ReconciliationDiff> Diffs { get; } = new();
        public (Guid diffId, ReconciliationDiffResolution resolution, DateTimeOffset? at, string? note)? LastResolutionUpdate;

        public Task<ReconciliationDiff?> GetDiffByIdAsync(Guid diffId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationDiff?>(Diffs.TryGetValue(diffId, out var d) ? d : null);

        public Task UpdateDiffResolutionAsync(
            Guid diffId, ReconciliationDiffResolution resolution, DateTimeOffset? resolvedAt, string? note, CancellationToken ct = default)
        {
            LastResolutionUpdate = (diffId, resolution, resolvedAt, note);
            if (Diffs.TryGetValue(diffId, out var d))
                Diffs[diffId] = d with { Resolution = resolution, ResolvedAt = resolvedAt, Note = note };
            return Task.CompletedTask;
        }

        // Unused in these tests:
        public Task<IReadOnlyList<ReconciliationSession>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationSession>>(Array.Empty<ReconciliationSession>());
        public Task<ReconciliationSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ReconciliationSession?>(null);
        public Task AddAsync(ReconciliationSession s, IReadOnlyList<ImportPreviewRow> rows, IReadOnlyList<ReconciliationDiff> diffs, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ImportPreviewRow>> GetStatementRowsAsync(Guid sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ImportPreviewRow>>(Array.Empty<ImportPreviewRow>());
        public Task UpdateStatusAsync(Guid id, ReconciliationStatus status, string? note, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ReconciliationDiff>> GetDiffsAsync(Guid sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationDiff>>(Diffs.Values.ToList());
        public Task ReplaceDiffsAsync(Guid sessionId, IReadOnlyList<ReconciliationDiff> diffs, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTrades : ITradeRepository
    {
        public Dictionary<Guid, Trade> Store { get; } = new();
        public List<Guid> Removed { get; } = new();
        public List<Trade> Updated { get; } = new();

        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Trade?>(Store.TryGetValue(id, out var t) ? t : null);

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Removed.Add(id);
            Store.Remove(id);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Trade trade, CancellationToken ct = default)
        {
            Updated.Add(trade);
            Store[trade.Id] = trade;
            return Task.CompletedTask;
        }

        // Unused in these tests:
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Trade>>(Store.Values.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Trade>>(Array.Empty<Trade>());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Trade>>(Array.Empty<Trade>());
        public Task AddAsync(Trade trade, CancellationToken ct = default) { Store[trade.Id] = trade; return Task.CompletedTask; }
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeApplier : IImportRowApplier
    {
        public List<(ImportPreviewRow row, ImportSourceKind sourceKind, ImportApplyOptions options)> Calls { get; } = new();
        public Guid? NextResultId { get; set; } = Guid.NewGuid();

        public Task<Guid?> ApplyAsync(
            ImportPreviewRow row, ImportSourceKind sourceKind, ImportApplyOptions options,
            IList<string>? warnings = null, CancellationToken ct = default)
        {
            Calls.Add((row, sourceKind, options));
            return Task.FromResult(NextResultId);
        }
    }
}
