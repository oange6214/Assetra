using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IPortfolioSummaryService
{
    PortfolioSummaryResult Calculate(PortfolioSummaryInput input);
}
