using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ILoanScheduleRepository
{
    Task<IReadOnlyList<LoanScheduleEntry>> GetByAssetAsync(Guid assetId);
    Task BulkInsertAsync(IEnumerable<LoanScheduleEntry> entries);
    Task MarkPaidAsync(Guid id, DateTime paidAt, Guid tradeId);
    Task DeleteByAssetAsync(Guid assetId);
}
