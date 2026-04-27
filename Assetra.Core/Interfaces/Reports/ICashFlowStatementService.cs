using Assetra.Core.Models.Reports;

namespace Assetra.Core.Interfaces.Reports;

public interface ICashFlowStatementService
{
    Task<CashFlowStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default);
}
