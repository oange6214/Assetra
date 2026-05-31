using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.PortfolioGroups;

/// <summary>
/// In-process cache of <see cref="PortfolioGroup"/> rows exposed as an
/// <see cref="ObservableCollection{T}"/> so multiple WPF surfaces (trade dialogs,
/// goal dialog, positions filter, FIRE filter) can bind to the same list without
/// each one re-querying the repository or holding stale data.
/// Singleton — instantiated by <see cref="PortfolioGroupsServiceCollectionExtensions"/>.
/// <see cref="PortfolioGroupsViewModel"/> mutates the underlying repo and is
/// responsible for invoking <see cref="RefreshAsync"/> after add / update / delete.
/// </summary>
public sealed class PortfolioGroupCatalog
{
    private readonly IPortfolioGroupRepository _repository;
    private readonly ObservableCollection<PortfolioGroup> _groups = new();
    private bool _loaded;

    public PortfolioGroupCatalog(IPortfolioGroupRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Groups = new ReadOnlyObservableCollection<PortfolioGroup>(_groups);
    }

    public ReadOnlyObservableCollection<PortfolioGroup> Groups { get; }

    public PortfolioGroup? Default => _groups.FirstOrDefault(g => g.Id == PortfolioGroup.DefaultId);

    public PortfolioGroup? FindById(Guid? id) =>
        id is null ? null : _groups.FirstOrDefault(g => g.Id == id.Value);

    /// <summary>
    /// Reloads from repository. Idempotent and safe to call on every dialog-open.
    /// Re-fires only when the list contents change.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var fresh = await _repository.GetAllAsync(ct).ConfigureAwait(false);

        // Cheap diff: same length + same Ids in order → assume identical.
        if (_loaded && fresh.Count == _groups.Count)
        {
            var allMatch = true;
            for (var i = 0; i < fresh.Count; i++)
            {
                if (fresh[i].Id != _groups[i].Id ||
                    fresh[i].Name != _groups[i].Name ||
                    fresh[i].ColorHex != _groups[i].ColorHex)
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
                return;
        }

        _groups.Clear();
        foreach (var g in fresh)
            _groups.Add(g);
        _loaded = true;
    }

    /// <summary>Loads on first call; subsequent calls are no-ops.</summary>
    public Task EnsureLoadedAsync(CancellationToken ct = default) =>
        _loaded ? Task.CompletedTask : RefreshAsync(ct);
}
