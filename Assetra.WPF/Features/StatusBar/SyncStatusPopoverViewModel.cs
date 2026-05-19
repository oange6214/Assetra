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
    // 由 host (MainViewModel) 注入的「導航到 Settings 頁」callback。
    // 設成可選：unit-test 沒提供時點按鈕不會崩，僅 no-op。
    private readonly Action? _navigateToSettings;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSyncedDisplay))]
    private DateTimeOffset? _lastSyncedAt;

    /// <summary>true 時 popover 顯示「未啟用同步」banner、主按鈕變成「前往啟用同步」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSyncEnabled))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonLabelKey))]
    private bool _isSyncDisabled;

    /// <summary>方便 XAML 用的反向：banner 用 IsSyncDisabled，內容區用 IsSyncEnabled。</summary>
    public bool IsSyncEnabled => !IsSyncDisabled;

    /// <summary>
    /// 主按鈕的 DynamicResource key：disabled 時導向設定、enabled 時觸發同步。
    /// </summary>
    public string PrimaryButtonLabelKey
        => IsSyncDisabled ? "Sync.Popover.GoToSettings" : "Sync.Popover.TriggerNow";

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
        BackgroundSyncTrigger? trigger = null,
        Action? navigateToSettings = null)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(localization);
        _sync = sync;
        _localization = localization;
        _trigger = trigger;
        _navigateToSettings = navigateToSettings;

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
        IsSyncDisabled = current.State == Core.Models.Sync.GlobalSyncState.Disabled;
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

    /// <summary>
    /// Popover 主按鈕的 unified handler：
    /// <list type="bullet">
    ///   <item>同步未啟用 → 導航到 Settings 頁讓使用者開啟同步（UX 修：以前點了沒反應）</item>
    ///   <item>已啟用 → 觸發 background loop 立即跑一次</item>
    /// </list>
    /// 不論哪種情況都關閉 popover 給點擊有 feedback。
    /// </summary>
    [RelayCommand]
    private void PrimaryAction()
    {
        if (IsSyncDisabled)
        {
            _navigateToSettings?.Invoke();
        }
        else
        {
            _trigger?.Request();
        }
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
