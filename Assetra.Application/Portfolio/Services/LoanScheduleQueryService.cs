using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class LoanScheduleQueryService : ILoanScheduleQueryService
{
    private readonly ILoanScheduleRepository _repository;

    public LoanScheduleQueryService(ILoanScheduleRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<LoanScheduleEntry>> GetByAssetAsync(
        Guid assetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _repository.GetByAssetAsync(assetId).ConfigureAwait(false);
    }
}
