using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IFinancialOverviewQueryService
{
    Task<FinancialOverviewResult> BuildAsync(
        IReadOnlyList<FinancialOverviewInvestmentItem> investments,
        CancellationToken ct = default);
}
