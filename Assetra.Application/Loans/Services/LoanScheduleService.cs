using Assetra.Application.Loans.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Loans.Services;

public sealed class LoanScheduleService : ILoanScheduleService
{
    private readonly ILoanScheduleRepository _repo;

    public LoanScheduleService(ILoanScheduleRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<LoanScheduleEntry>> GetScheduleByAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _repo.ClearPaidWithoutActiveTradeAsync(assetId).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await _repo.ReconcilePaidFromActiveRepaymentsAsync(assetId).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await _repo.GetByAssetAsync(assetId).ConfigureAwait(false);
    }
}
