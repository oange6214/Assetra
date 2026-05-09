using System.Collections.ObjectModel;
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

    public AuditLogViewModel(ITradeAuditRepository? audit = null)
    {
        _audit = audit;
        Entries = new ReadOnlyObservableCollection<AuditRowViewModel>(_entries);
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
}
