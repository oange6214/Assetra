using System.Collections.ObjectModel;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Settings;

/// <summary>
/// 顯示並讓使用者解決 LWW resolver 無法自動處理的 sync conflicts。
/// <para>
/// MVP 行為：列出所有 manual conflict（local vs remote envelope），讓使用者逐筆選擇
/// **保留 local**（push remote 的 entityId 用 local payload）或 **採用 remote**
/// （透過 composite queue 依 EntityType 分派 ApplyRemote）。
/// </para>
/// <para>
/// Conflicts 來源：<see cref="IManualConflictDrain"/>（Composite 會聚合所有 entity-specific queue）。
/// </para>
/// </summary>
public partial class ConflictResolutionViewModel : ObservableObject
{
    private readonly IManualConflictDrain _drain;
    private readonly ILocalChangeQueue _queue;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<ConflictRowViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    public ConflictResolutionViewModel(IManualConflictDrain drain, ILocalChangeQueue queue)
    {
        ArgumentNullException.ThrowIfNull(drain);
        ArgumentNullException.ThrowIfNull(queue);
        _drain = drain;
        _queue = queue;
    }

    [RelayCommand]
    private void Reload()
    {
        Items.Clear();
        foreach (var c in _drain.DrainManualConflicts())
            Items.Add(new ConflictRowViewModel(c));
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = Items.Count == 0 ? "No pending conflicts." : $"{Items.Count} pending.";
    }

    [RelayCommand]
    private Task KeepLocalAsync(ConflictRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // Keep local: do nothing to DB — local row is already pending push from before sync;
        // next SyncAsync will push it. Just drop from manual-conflict list.
        Items.Remove(row);
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = $"Kept local for {row.EntityId}.";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task UseRemoteAsync(ConflictRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        await _queue.ApplyRemoteAsync(new[] { row.Conflict.Remote }).ConfigureAwait(false);
        Items.Remove(row);
        OnPropertyChanged(nameof(HasItems));
        StatusMessage = $"Adopted remote for {row.EntityId}.";
    }
}

public sealed class ConflictRowViewModel
{
    public ConflictRowViewModel(SyncConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        Conflict = conflict;
    }

    public SyncConflict Conflict { get; }
    public Guid EntityId => Conflict.EntityId;
    public string EntityType => Conflict.EntityType;
    public long LocalVersion => Conflict.Local.Version.Version;
    public long RemoteVersion => Conflict.Remote.Version.Version;
    public DateTimeOffset LocalAt => Conflict.Local.Version.LastModifiedAt;
    public DateTimeOffset RemoteAt => Conflict.Remote.Version.LastModifiedAt;
    public string LocalDevice => Conflict.Local.Version.LastModifiedByDevice;
    public string RemoteDevice => Conflict.Remote.Version.LastModifiedByDevice;
    public bool LocalDeleted => Conflict.Local.Deleted;
    public bool RemoteDeleted => Conflict.Remote.Deleted;
}
