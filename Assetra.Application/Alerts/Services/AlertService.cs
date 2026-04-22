using Assetra.Application.Alerts.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Alerts.Services;

public sealed class AlertService : IAlertService
{
    private readonly IAlertRepository _repo;

    public AlertService(IAlertRepository repo) => _repo = repo;

    public Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default) =>
        _repo.GetRulesAsync();

    public Task AddAsync(AlertRule rule, CancellationToken ct = default) =>
        _repo.AddAsync(rule);

    public Task UpdateAsync(AlertRule rule, CancellationToken ct = default) =>
        _repo.UpdateAsync(rule);

    public Task RemoveAsync(Guid id, CancellationToken ct = default) =>
        _repo.RemoveAsync(id);
}
