using Assetra.Application.Reports;
using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsContext(this IServiceCollection services)
    {
        services.AddSingleton<Assetra.Application.Reports.Services.MonthEndReportService>();
        services.AddSingleton<IIncomeStatementService, IncomeStatementService>();
        services.AddSingleton<IBalanceSheetService, BalanceSheetService>();
        services.AddSingleton<ICashFlowStatementService, CashFlowStatementService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<Assetra.WPF.Features.Reports.ReportsViewModel>();
        return services;
    }
}
