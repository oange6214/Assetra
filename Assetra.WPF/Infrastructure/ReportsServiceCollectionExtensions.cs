using Assetra.Application.Reports;
using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsContext(this IServiceCollection services)
    {
        services.AddSingleton<Assetra.Application.Reports.Services.MonthEndReportService>();
        services.AddSingleton<IIncomeStatementService, IncomeStatementService>();
        services.AddSingleton<IBalanceSheetService>(sp => new BalanceSheetService(
            sp.GetRequiredService<IAssetRepository>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetService<IPortfolioSnapshotRepository>(),
            sp.GetService<IMultiCurrencyValuationService>(),
            sp.GetService<IAppSettingsService>()?.Current.BaseCurrency));
        services.AddSingleton<ICashFlowStatementService, CashFlowStatementService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<Assetra.WPF.Features.Reports.ReportsViewModel>();
        return services;
    }
}
