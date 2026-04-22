using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioLoadService
{
    Task<PortfolioLoadResult> LoadAsync(CancellationToken ct = default);
}
