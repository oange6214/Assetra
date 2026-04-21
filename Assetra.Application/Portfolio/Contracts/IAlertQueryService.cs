using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IAlertQueryService
{
    Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default);
}
