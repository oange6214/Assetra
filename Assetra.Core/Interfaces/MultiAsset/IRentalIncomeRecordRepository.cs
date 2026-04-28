using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRentalIncomeRecordRepository
{
    Task<IReadOnlyList<RentalIncomeRecord>> GetByPropertyAsync(Guid realEstateId, CancellationToken ct = default);
    Task<IReadOnlyList<RentalIncomeRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task AddAsync(RentalIncomeRecord record, CancellationToken ct = default);
    Task UpdateAsync(RentalIncomeRecord record, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
