using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IFinancialOverviewQueryService
{
    Task<FinancialOverviewResult> BuildAsync(
        IReadOnlyList<FinancialOverviewInvestmentItem> investments,
        CancellationToken ct = default);
}
