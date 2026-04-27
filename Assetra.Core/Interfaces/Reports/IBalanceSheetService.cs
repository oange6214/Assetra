using Assetra.Core.Models.Reports;

namespace Assetra.Core.Interfaces.Reports;

public interface IBalanceSheetService
{
    Task<BalanceSheet> GenerateAsync(DateOnly asOf, CancellationToken ct = default);
}
