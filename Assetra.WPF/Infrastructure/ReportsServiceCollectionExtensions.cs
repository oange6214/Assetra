using Assetra.Application.Reports;
using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsContext(this IServiceCollection services)
    {
        services.AddSingleton<Assetra.Application.Reports.Services.MonthEndReportService>();
        services.AddSingleton<IIncomeStatementService>(sp => new IncomeStatementService(
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetService<ICategoryRepository>(),
            sp.GetService<IRentalIncomeRecordRepository>(),
            sp.GetService<IInsurancePremiumRecordRepository>()));
        services.AddSingleton<IBalanceSheetService>(sp => new BalanceSheetService(
            sp.GetRequiredService<IAssetRepository>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetService<IPortfolioSnapshotRepository>(),
            sp.GetService<IMultiCurrencyValuationService>(),
            sp.GetService<IAppSettingsService>(),
            sp.GetService<IRealEstateRepository>(),
            sp.GetService<IInsurancePolicyRepository>(),
            sp.GetService<IRetirementAccountRepository>(),
            sp.GetService<IPhysicalAssetRepository>()));
        services.AddSingleton<ICashFlowStatementService, CashFlowStatementService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        // 稅務 — Stage 1+2+3 (v0.28)：歷年稅制 profile + AMT/IncomeTax 完整試算。
        // EmbeddedTaxProfileProvider 從 Assetra.Core/Resources/TaxYearProfiles.json 載入。
        services.AddSingleton<ITaxProfileProvider, Assetra.Application.Tax.EmbeddedTaxProfileProvider>();
        services.AddSingleton<Assetra.Application.Tax.AnnualTaxComputationService>();
        services.AddSingleton<Assetra.WPF.Features.Reports.ReportsViewModel>();
        return services;
    }
}
