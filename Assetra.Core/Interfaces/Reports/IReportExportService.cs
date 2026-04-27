using Assetra.Core.Models.Reports;

namespace Assetra.Core.Interfaces.Reports;

public interface IReportExportService
{
    Task ExportAsync(IncomeStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(BalanceSheet statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(CashFlowStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);
}
