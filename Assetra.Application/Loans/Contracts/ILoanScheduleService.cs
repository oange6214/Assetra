using Assetra.Core.Models;

namespace Assetra.Application.Loans.Contracts;

public interface ILoanScheduleService
{
    Task<IReadOnlyList<LoanScheduleEntry>> GetScheduleByAssetAsync(Guid assetId, CancellationToken ct = default);
}
