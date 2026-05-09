using Assetra.Core.Models;
using Assetra.Core.Models.Reports;

namespace Assetra.Core.Interfaces.Reports;

public interface IReportExportService
{
    Task ExportAsync(IncomeStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(BalanceSheet statement, ExportFormat format, string filePath, CancellationToken ct = default);
    Task ExportAsync(CashFlowStatement statement, ExportFormat format, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Annual <see cref="TaxSummary"/> export. CSV layout: per-record dividend
    /// + capital-gain rows followed by the four bucket totals + AMT trigger
    /// flag. PDF: split into two sections (股利所得 / 已實現資本利得) with
    /// totals + the overseas-income / AMT footer.
    /// </summary>
    Task ExportAsync(TaxSummary summary, ExportFormat format, string filePath, CancellationToken ct = default);
}
