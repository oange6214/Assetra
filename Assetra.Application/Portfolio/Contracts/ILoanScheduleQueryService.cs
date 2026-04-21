using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Contracts;

public interface ILoanScheduleQueryService
{
    Task<IReadOnlyList<LoanScheduleEntry>> GetByAssetAsync(
        Guid assetId,
        CancellationToken ct = default);
}
