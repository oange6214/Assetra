using Assetra.Core.Models.Reports;

namespace Assetra.Core.Interfaces.Reports;

public interface IIncomeStatementService
{
    Task<IncomeStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default);
}
