using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.StatusBar;

/// <summary>
/// Sync-Status-Indicator Phase 2 popover. Shows a localized per-domain
/// breakdown of pending pushes + "立即同步" button. Bound from the status
/// bar's clickable sync chip.
/// </summary>
public sealed partial class SyncStatusPopoverViewModel : ObservableObject, IDisposable
{
    private readonly IGlobalSyncStatusService _sync;
    private readonly ILocalizationService _localization;
    private readonly BackgroundSyncTrigger? _trigger;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSyncedDisplay))]
    private DateTimeOffset? _lastSyncedAt;

    public ObservableCollection<SyncDomainRow> Rows { get; } = new();

    public string LastSyncedDisplay
    {
        get
        {
            if (LastSyncedAt is null)
                return _localization.Get("Sync.Popover.NeverSynced", "尚未同步過");
            var local = LastSyncedAt.Value.LocalDateTime;
            return string.Format(
                _localization.Get("Sync.Popover.LastSyncedFormat", "上次同步：{0:yyyy-MM-dd HH:mm}"),
                local);
        }
    }

    public SyncStatusPopoverViewModel(
        IGlobalSyncStatusService sync,
        ILocalizationService localization,
        BackgroundSyncTrigger? trigger = null)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(localization);
        _sync = sync;
        _localization = localization;
        _trigger = trigger;

        _sync.Changed += OnSyncChanged;
        // Seed from current snapshot in case the popover opens before any change event.
        ApplySnapshot();
    }

    private void OnSyncChanged(object? sender, GlobalSyncSnapshot snapshot)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(ApplySnapshot);
            return;
        }
        ApplySnapshot();
    }

    private void ApplySnapshot()
    {
        var current = _sync.Current;
        LastSyncedAt = current.LastSyncedAt;
        var domains = _sync.GetPerDomain();

        // Reconcile rows: update existing, add new, remove stale. ItemsControl
        // re-binds in place which avoids visual flicker on poll intervals.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in domains)
        {
            seenKeys.Add(d.DomainKey);
            var existing = Rows.FirstOrDefault(r => r.DomainKey == d.DomainKey);
            if (existing is null)
            {
                Rows.Add(new SyncDomainRow(
                    d.DomainKey,
                    ResolveDomainLabel(d.DomainKey),
                    d.PendingCount));
            }
            else
            {
                existing.PendingCount = d.PendingCount;
            }
        }
        // Drop rows for domains the service no longer reports.
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!seenKeys.Contains(Rows[i].DomainKey))
                Rows.RemoveAt(i);
    }

    /// <summary>Map stable domain key → localized label. Falls back to the key itself.</summary>
    private string ResolveDomainLabel(string key)
        => _localization.Get($"Sync.Domain.{key}", key);

    [RelayCommand]
    private void TriggerSync()
    {
        _trigger?.Request();
        // Close the popover so the user gets feedback that the click did something;
        // the indicator dot will spin briefly when the actual sync kicks off.
        IsOpen = false;
    }

    public void Dispose()
    {
        _sync.Changed -= OnSyncChanged;
    }
}

/// <summary>One row in the popover. Mutable so the VM can update PendingCount in place.</summary>
public sealed partial class SyncDomainRow : ObservableObject
{
    public string DomainKey { get; }
    public string LocalizedName { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSynced))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    private int _pendingCount;

    public bool IsSynced => PendingCount == 0;
    public string StatusIcon => IsSynced ? "Checkmark24" : "ArrowSync24";

    public SyncDomainRow(string domainKey, string localizedName, int pendingCount)
    {
        DomainKey = domainKey;
        LocalizedName = localizedName;
        _pendingCount = pendingCount;
    }
}

/// <summary>
/// Thin abstraction over <see cref="Assetra.WPF.Infrastructure.BackgroundSyncService.RequestImmediateSync"/>
/// so the VM can stay testable. DI registers the real one as a delegate.
/// </summary>
public sealed class BackgroundSyncTrigger
{
    private readonly Action _trigger;
    public BackgroundSyncTrigger(Action trigger) => _trigger = trigger;
    public void Request() => _trigger();
}
