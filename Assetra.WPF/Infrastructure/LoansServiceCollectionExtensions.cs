using Assetra.Application.Loans.Contracts;
using Assetra.Application.Loans.Services;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class LoansServiceCollectionExtensions
{
    public static IServiceCollection AddLoansContext(
        this IServiceCollection services,
        string dbPath)
    {
        services.AddSingleton<ILoanScheduleRepository>(_ => new LoanScheduleSqliteRepository(dbPath));
        services.AddSingleton<ILoanScheduleService, LoanScheduleService>();
        services.AddSingleton<ILoanPaymentWorkflowService>(sp =>
            new LoanPaymentWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<ILoanScheduleRepository>()));
        services.AddSingleton<ILoanMutationWorkflowService>(sp =>
            new LoanMutationWorkflowService(
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<ILoanScheduleRepository>(),
                sp.GetRequiredService<ITransactionService>()));
        return services;
    }
}
