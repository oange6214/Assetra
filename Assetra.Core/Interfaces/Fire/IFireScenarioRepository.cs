using Assetra.Core.Models.Fire;

namespace Assetra.Core.Interfaces.Fire;

public interface IFireScenarioRepository
{
    Task<IReadOnlyList<FireScenario>> GetAllAsync(CancellationToken ct = default);

    Task<FireScenario?> GetDefaultAsync(CancellationToken ct = default);

    Task<FireScenario?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FireCashFlowEvent>> GetCashFlowEventsAsync(
        Guid scenarioId,
        CancellationToken ct = default);

    Task UpsertAsync(
        FireScenario scenario,
        IReadOnlyList<FireCashFlowEvent> cashFlowEvents,
        CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
