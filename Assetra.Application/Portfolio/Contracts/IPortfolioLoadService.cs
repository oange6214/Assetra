using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IPortfolioLoadService
{
    Task<PortfolioLoadResult> LoadAsync(CancellationToken ct = default);
}
