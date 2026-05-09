using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.AuditLog;

/// <summary>
/// 顯示 trade_audit 資料表內容（最近 100 筆）。
/// 提供使用者一個「我之前刪了什麼？」的回顧視窗，避免要靠 SQL 查表才能看到。
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

    /// <summary>ICollectionView used by the DataGrid; honours <see cref="FilterText"/>.</summary>
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

    [RelayCommand]
    private async Task RestoreAsync(AuditRowViewModel? row)
    {
        if (row is null || _restore is null) return;
        try
        {
            var newId = await _restore.RestoreAsync(row.RawTradeJson).ConfigureAwait(true);
            _snackbar?.Success($"已還原為新 Trade（Id={newId.ToString()[..8]}…）。請至交易紀錄頁複查並刪除舊的若有需要。");
        }
        catch (Exception ex)
        {
            _snackbar?.Error("還原失敗：" + ex.Message);
        }
    }

    partial void OnFilterTextChanged(string value) => EntriesView.Refresh();

    private bool FilterEntry(object obj)
    {
        if (obj is not AuditRowViewModel row) return false;
        var q = FilterText?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return row.Action.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.TradeIdShort.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Note.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.TradeJsonPreview.Contains(q, StringComparison.OrdinalIgnoreCase);
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
}

/// <summary>單筆 audit row 的顯示包裝（提供 formatted timestamp + truncated json preview）。</summary>
public sealed class AuditRowViewModel
{
    private readonly TradeAuditEntry _entry;
    public AuditRowViewModel(TradeAuditEntry entry) => _entry = entry;

    public DateTime RecordedAt => _entry.RecordedAt.ToLocalTime();
    public string RecordedAtDisplay => RecordedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string Action => _entry.Action;
    public Guid TradeId => _entry.TradeId;
    public string TradeIdShort => _entry.TradeId.ToString()[..8];
    public string Note => _entry.Note ?? string.Empty;

    /// <summary>JSON 預覽截短到 600 字 — 完整 payload 在 trade_audit table 中。</summary>
    public string TradeJsonPreview =>
        _entry.TradeJson.Length <= 600 ? _entry.TradeJson : _entry.TradeJson[..600] + "…";

    /// <summary>Full snapshot for the restore-from-snapshot path.</summary>
    public string RawTradeJson => _entry.TradeJson;
}
