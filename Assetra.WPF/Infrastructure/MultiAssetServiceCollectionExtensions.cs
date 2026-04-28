using Assetra.Application.MultiAsset;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Insurance;
using Assetra.WPF.Features.PhysicalAsset;
using Assetra.WPF.Features.RealEstate;
using Assetra.WPF.Features.Retirement;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class MultiAssetServiceCollectionExtensions
{
    public static IServiceCollection AddMultiAssetContext(
        this IServiceCollection services,
        string dbPath)
    {
        // Repositories (dual-role: IRealEstateRepository + IRealEstateSyncStore)
        services.AddSingleton<RealEstateSqliteRepository>(_ => new RealEstateSqliteRepository(dbPath));
        services.AddSingleton<IRealEstateRepository>(sp => sp.GetRequiredService<RealEstateSqliteRepository>());
        services.AddSingleton<IRealEstateSyncStore>(sp => sp.GetRequiredService<RealEstateSqliteRepository>());

        services.AddSingleton<IRentalIncomeRecordRepository>(_ => new RentalIncomeRecordSqliteRepository(dbPath));

        services.AddSingleton<InsurancePolicySqliteRepository>(_ => new InsurancePolicySqliteRepository(dbPath));
        services.AddSingleton<IInsurancePolicyRepository>(sp => sp.GetRequiredService<InsurancePolicySqliteRepository>());
        services.AddSingleton<IInsurancePolicySyncStore>(sp => sp.GetRequiredService<InsurancePolicySqliteRepository>());

        services.AddSingleton<IInsurancePremiumRecordRepository>(_ => new InsurancePremiumRecordSqliteRepository(dbPath));

        // Retirement (v0.24)
        services.AddSingleton<RetirementAccountSqliteRepository>(_ => new RetirementAccountSqliteRepository(dbPath));
        services.AddSingleton<IRetirementAccountRepository>(sp => sp.GetRequiredService<RetirementAccountSqliteRepository>());
        services.AddSingleton<IRetirementAccountSyncStore>(sp => sp.GetRequiredService<RetirementAccountSqliteRepository>());
        services.AddSingleton<IRetirementContributionRepository>(_ => new RetirementContributionSqliteRepository(dbPath));

        // Physical asset (v0.24)
        services.AddSingleton<PhysicalAssetSqliteRepository>(_ => new PhysicalAssetSqliteRepository(dbPath));
        services.AddSingleton<IPhysicalAssetRepository>(sp => sp.GetRequiredService<PhysicalAssetSqliteRepository>());
        services.AddSingleton<IPhysicalAssetSyncStore>(sp => sp.GetRequiredService<PhysicalAssetSqliteRepository>());

        // Application services
        services.AddSingleton<IRealEstateValuationService, RealEstateValuationService>();
        services.AddSingleton<IInsuranceCashValueCalculator, InsuranceCashValueCalculator>();
        services.AddSingleton<IRetirementProjectionService, RetirementProjectionService>();
        services.AddSingleton<IPhysicalAssetValuationService, PhysicalAssetValuationService>();

        // Sync local change queues
        services.AddSingleton<RealEstateLocalChangeQueue>(sp =>
            new RealEstateLocalChangeQueue(sp.GetRequiredService<IRealEstateSyncStore>()));
        services.AddSingleton<InsurancePolicyLocalChangeQueue>(sp =>
            new InsurancePolicyLocalChangeQueue(sp.GetRequiredService<IInsurancePolicySyncStore>()));
        services.AddSingleton<RetirementAccountLocalChangeQueue>(sp =>
            new RetirementAccountLocalChangeQueue(sp.GetRequiredService<IRetirementAccountSyncStore>()));
        services.AddSingleton<PhysicalAssetLocalChangeQueue>(sp =>
            new PhysicalAssetLocalChangeQueue(sp.GetRequiredService<IPhysicalAssetSyncStore>()));

        // ViewModels
        services.AddSingleton<RealEstateViewModel>();
        services.AddSingleton<InsurancePolicyViewModel>();
        services.AddSingleton<RetirementViewModel>();
        services.AddSingleton<PhysicalAssetViewModel>();

        return services;
    }
}
