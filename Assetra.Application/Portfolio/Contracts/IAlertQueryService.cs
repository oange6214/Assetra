using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAlertQueryService
{
    Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default);
}
