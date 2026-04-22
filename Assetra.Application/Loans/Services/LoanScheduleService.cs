using Assetra.Application.Loans.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Loans.Services;

public sealed class LoanScheduleService : ILoanScheduleService
{
    private readonly ILoanScheduleRepository _repo;

    public LoanScheduleService(ILoanScheduleRepository repo) => _repo = repo;

    public Task<IReadOnlyList<LoanScheduleEntry>> GetScheduleByAssetAsync(Guid assetId, CancellationToken ct = default) =>
        _repo.GetByAssetAsync(assetId);
}
