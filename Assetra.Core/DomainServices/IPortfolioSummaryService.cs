using Assetra.Core.Dtos;

namespace Assetra.Core.DomainServices;

public interface IPortfolioSummaryService
{
    PortfolioSummaryResult Calculate(PortfolioSummaryInput input);
}
