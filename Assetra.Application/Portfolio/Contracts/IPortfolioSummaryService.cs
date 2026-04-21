using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioSummaryService
{
    PortfolioSummaryResult Calculate(PortfolioSummaryInput input);
}
