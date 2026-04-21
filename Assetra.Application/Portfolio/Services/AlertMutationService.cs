using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class AlertMutationService : IAlertMutationService
{
    private readonly IAlertRepository _repository;

    public AlertMutationService(IAlertRepository repository)
    {
        _repository = repository;
    }

    public Task AddAsync(AlertRule rule, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _repository.AddAsync(rule);
    }

    public Task UpdateAsync(AlertRule rule, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _repository.UpdateAsync(rule);
    }

    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _repository.RemoveAsync(id);
    }
}
