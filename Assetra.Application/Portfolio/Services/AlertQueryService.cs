using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class AlertQueryService : IAlertQueryService
{
    private readonly IAlertRepository _repository;

    public AlertQueryService(IAlertRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _repository.GetRulesAsync();
    }
}
