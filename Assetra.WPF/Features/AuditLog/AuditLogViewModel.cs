using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.AuditLog;

/// <summary>
/// 顯示 trade_audit 資料表內容（最近 100 筆），master-detail 介面：
/// 左側列表選一筆，右側 <see cref="TradeDetailCardView"/> 渲染為人話。
/// 提供 quick filter chips（All / Delete / Edit / 24h）與還原確認。
/// </summary>
public sealed partial class AuditLogViewModel : ObservableObject
{
    private readonly ITradeAuditRepository? _audit;
    private readonly Application.Portfolio.Services.TradeAuditRestoreService? _restore;
    private readonly Infrastructure.ISnackbarService? _snackbar;
    private readonly ObservableCollection<AuditRowViewModel> _entries = [];
    public ReadOnlyObservableCollection<AuditRowViewModel> Entries { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntries))]
    [NotifyPropertyChangedFor(nameof(HasNoEntries))]
    private int _entryCount;

    public bool HasEntries => EntryCount > 0;
    public bool HasNoEntries => EntryCount == 0;
    public bool IsAvailable => _audit is not null;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;

    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>Quick-filter chips: <c>"All"</c> / <c>"Delete"</c> / <c>"Edit"</c> / <c>"24h"</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChipAll))]
    [NotifyPropertyChangedFor(nameof(IsChipDelete))]
    [NotifyPropertyChangedFor(nameof(IsChipEdit))]
    [NotifyPropertyChangedFor(nameof(IsChipDay))]
    private string _activeChip = "All";

    public bool IsChipAll => ActiveChip == "All";
    public bool IsChipDelete => ActiveChip == "Delete";
    public bool IsChipEdit => ActiveChip == "Edit";
    public bool IsChipDay => ActiveChip == "24h";

    /// <summary>Currently selected row in the master grid; drives the right-pane detail card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private AuditRowViewModel? _selectedEntry;

    public bool HasSelection => SelectedEntry is not null;

    /// <summary>
    /// Detail-pane VM resolved from <see cref="SelectedEntry"/>. When the
    /// selected row is one half of an edit-replace pair we look back one row
    /// in the loaded list to find the previous snapshot, enabling diff mode.
    /// </summary>
    public TradeDetailViewModel Detail
    {
        get
        {
            if (SelectedEntry is null) return new TradeDetailViewModel(string.Empty, snapshot: null);
            var current = TradeSnapshotParser.TryParse(SelectedEntry.RawTradeJson);
            var previous = ResolvePreviousForDiff(SelectedEntry);
            return new TradeDetailViewModel(SelectedEntry.RawTradeJson, current, previous);
        }
    }

    /// <summary>ICollectionView used by the DataGrid; honours <see cref="FilterText"/> + <see cref="ActiveChip"/>.</summary>
    public ICollectionView EntriesView { get; }

    public AuditLogViewModel(
        ITradeAuditRepository? audit = null,
        Application.Portfolio.Services.TradeAuditRestoreService? restore = null,
        Infrastructure.ISnackbarService? snackbar = null)
    {
        _audit = audit;
        _restore = restore;
        _snackbar = snackbar;
        Entries = new ReadOnlyObservableCollection<AuditRowViewModel>(_entries);
        EntriesView = CollectionViewSource.GetDefaultView(_entries);
        EntriesView.Filter = FilterEntry;
    }

    /// <summary>Whether the restore button should be shown (DI may not have wired the service).</summary>
    public bool CanRestore => _restore is not null;

    /// <summary>
    /// Confirmation flow: clicking the master-list "還原" toggles a preview
    /// state. Confirming triggers the actual insert; cancelling clears it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestorePreviewActive))]
    [NotifyPropertyChangedFor(nameof(ShouldShowRestoreButton))]
    private AuditRowViewModel? _pendingRestore;

    public bool IsRestorePreviewActive => PendingRestore is not null;
    public bool ShouldShowRestoreButton => CanRestore && !IsRestorePreviewActive;

    [RelayCommand]
    private void RequestRestore(AuditRowViewModel? row)
    {
        if (row is null || _restore is null) return;
        SelectedEntry = row;
        PendingRestore = row;
    }

    [RelayCommand]
    private void CancelRestore() => PendingRestore = null;

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        var row = PendingRestore;
        if (row is null || _restore is null) return;
        try
        {
            var newId = await _restore.RestoreAsync(row.RawTradeJson).ConfigureAwait(true);
            _snackbar?.Success($"已還原為新 Trade（Id={newId.ToString()[..8]}…）。請至交易紀錄頁複查。");
            PendingRestore = null;
        }
        catch (Exception ex)
        {
            _snackbar?.Error("還原失敗：" + ex.Message);
        }
    }

    [RelayCommand]
    private void SetChip(string? chip)
    {
        if (string.IsNullOrWhiteSpace(chip)) return;
        ActiveChip = chip;
        EntriesView.Refresh();
    }

    partial void OnFilterTextChanged(string value) => EntriesView.Refresh();
    partial void OnActiveChipChanged(string value) => EntriesView.Refresh();

    private bool FilterEntry(object obj)
    {
        if (obj is not AuditRowViewModel row) return false;

        // Chip filter
        switch (ActiveChip)
        {
            case "Delete":
                if (!row.Action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return false;
                break;
            case "Edit":
                if (!row.Action.Contains("edit", StringComparison.OrdinalIgnoreCase)) return false;
                break;
            case "24h":
                if ((DateTime.UtcNow - row.RawRecordedAtUtc).TotalHours > 24) return false;
                break;
        }

        var q = FilterText?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return row.Action.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.TradeIdShort.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Note.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.SummaryLine.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For an edit-replace audit row, the BEFORE snapshot is captured on the
    /// row itself; the AFTER state is the trade currently in the live table.
    /// Diff against the next-newer audit row of the same trade only when an
    /// older edit-replace exists (so multi-edit history shows incremental diffs).
    /// </summary>
    private Trade? ResolvePreviousForDiff(AuditRowViewModel row)
    {
        if (!row.Action.Contains("edit", StringComparison.OrdinalIgnoreCase)) return null;

        // Find the most recent older audit row for the same TradeId (older = earlier RecordedAt).
        AuditRowViewModel? older = null;
        foreach (var e in _entries)
        {
            if (e.TradeId != row.TradeId) continue;
            if (e.RawRecordedAtUtc >= row.RawRecordedAtUtc) continue;
            if (older is null || e.RawRecordedAtUtc > older.RawRecordedAtUtc) older = e;
        }
        return older is null ? null : TradeSnapshotParser.TryParse(older.RawTradeJson);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_audit is null || IsLoading) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var rows = await _audit.GetRecentAsync(limit: 100).ConfigureAwait(true);
            _entries.Clear();
            foreach (var e in rows)
                _entries.Add(new AuditRowViewModel(e));
            EntryCount = _entries.Count;
            // Auto-select the first row so the right pane isn't blank on load.
            SelectedEntry = _entries.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedEntryChanged(AuditRowViewModel? value)
    {
        // Clear pending restore on selection change so confirmation doesn't carry across rows.
        if (PendingRestore is not null && PendingRestore != value)
            PendingRestore = null;
    }
}

/// <summary>
/// 單筆 audit row 的顯示包裝。Now exposes a humanised <see cref="SummaryLine"/>
/// so the master grid can show「0056 Buy 5,000 @ $23.05」instead of raw JSON.
/// </summary>
public sealed class AuditRowViewModel
{
    private readonly TradeAuditEntry _entry;
    private readonly Trade? _parsed;

    public AuditRowViewModel(TradeAuditEntry entry)
    {
        _entry = entry;
        _parsed = TradeSnapshotParser.TryParse(entry.TradeJson);
    }

    public DateTime RawRecordedAtUtc => _entry.RecordedAt;
    public DateTime RecordedAt => _entry.RecordedAt.ToLocalTime();
    public string RecordedAtDisplay => RecordedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string Action => _entry.Action;
    public Guid TradeId => _entry.TradeId;
    public string TradeIdShort => _entry.TradeId.ToString()[..8];
    public string Note => _entry.Note ?? string.Empty;

    /// <summary>One-line domain summary built from the parsed snapshot.</summary>
    public string SummaryLine => TradeSnapshotParser.Summarize(_parsed);

    /// <summary>Full snapshot for the restore-from-snapshot path.</summary>
    public string RawTradeJson => _entry.TradeJson;
}
