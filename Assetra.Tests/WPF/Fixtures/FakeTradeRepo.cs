using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Tests.WPF.Fixtures;

/// <summary>
/// In-memory <see cref="ITradeRepository"/> for tests that need real trade-projection
/// behaviour (BalanceQueryService, TransactionService, PortfolioViewModel reload).
/// Exposes <see cref="Store"/> for direct seeding/inspection.
/// </summary>
internal sealed class FakeTradeRepo : ITradeRepository
{
    public List<Trade> Store { get; } = new();

    public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());

    public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Trade>>(
            Store.Where(t => t.CashAccountId == cashAccountId
                          || t.ToCashAccountId == cashAccountId).ToList());

    public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Trade>>(
            Store.Where(t => t.LoanLabel == loanLabel).ToList());

    public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));

    public Task AddAsync(Trade t, CancellationToken ct = default)
    {
        Store.Add(t);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Trade t, CancellationToken ct = default)
    {
        var i = Store.FindIndex(x => x.Id == t.Id);
        if (i >= 0) Store[i] = t;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        Store.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }

    public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        Store.RemoveAll(x => x.ParentTradeId == parentId);
        return Task.CompletedTask;
    }

    public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        Store.RemoveAll(x => x.CashAccountId == accountId || x.ToCashAccountId == accountId);
        return Task.CompletedTask;
    }

    public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default)
    {
        Store.RemoveAll(x =>
            (liabilityAssetId.HasValue && x.LiabilityAssetId == liabilityAssetId.Value) ||
            (!string.IsNullOrEmpty(loanLabel) && x.LoanLabel == loanLabel));
        return Task.CompletedTask;
    }

    public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default)
    {
        foreach (var m in mutations)
        {
            switch (m)
            {
                case AddTradeMutation add: Store.Add(add.Trade); break;
                case RemoveTradeMutation rem: Store.RemoveAll(t => t.Id == rem.Id); break;
            }
        }
        return Task.CompletedTask;
    }
}
