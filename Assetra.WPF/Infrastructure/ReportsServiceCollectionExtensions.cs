using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddReportsContext(this IServiceCollection services)
    {
        services.AddSingleton<Assetra.Application.Reports.Services.MonthEndReportService>();
        services.AddSingleton<Assetra.WPF.Features.Reports.ReportsViewModel>();
        return services;
    }
}
