using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;
using Assetra.Infrastructure.Import;
using Assetra.WPF.Features.Import;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Reconciliation;

/// <summary>
/// 對帳工作台 ViewModel：列出歷史 sessions、檢視選定 session 的 diffs、執行解決動作。
/// v0.10 起支援：新建對帳作業（既有 batch 或上傳新檔）、Created / OverwrittenFromStatement 動作、
/// 餘額對帳面板、Kind 分組顯示。
/// </summary>
public partial class ReconciliationViewModel : ObservableObject
{
    private readonly IReconciliationService _service;
    private readonly IReconciliationSessionRepository _sessions;
    private readonly IAssetRepository _assets;
    private readonly ITradeRepository _trades;
    private readonly IReconciliationMatcher _matcher;
    private readonly IImportBatchHistoryRepository? _history;
    private readonly IImportFormatDetector? _detector;
    private readonly ImportParserFactory? _parserFactory;
    private readonly ILocalizationService? _localization;
    private readonly ICurrencyService? _currency;
    private int _reloadVersion;

    public ObservableCollection<ReconciliationSession> Sessions { get; } = new();
    public ObservableCollection<ReconciliationDiffRowViewModel> Diffs { get; } = new();
    public ObservableCollection<CashAccountOption> AccountOptions { get; } = new();
    public ObservableCollection<BatchOption> BatchOptions { get; } = new();

    public ICollectionView GroupedDiffs { get; }

    [ObservableProperty]
    private ReconciliationSession? _selectedSession;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    // ─── 新建 session 面板狀態 ───
    [ObservableProperty]
    private bool _isNewSessionPanelOpen;

    [ObservableProperty]
    private CashAccountOption? _newSessionAccount;

    [ObservableProperty]
    private DateTime _newPeriodStart = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _newPeriodEnd = DateTime.Today;

    [ObservableProperty]
    private bool _useExistingBatch = true;

    [ObservableProperty]
    private bool _useUploadedFile;

    // Mutual-exclusion guards: each setter only mutates the other when they're
    // about to be in the same state. WPF RadioButton group + TwoWay bindings
    // already write both properties on click; these guards just stop a stack
    // overflow if the writes arrive out of order.
    partial void OnUseExistingBatchChanged(bool value)
    {
        if (value && UseUploadedFile)
            UseUploadedFile = false;
        else if (!value && !UseUploadedFile)
            UseUploadedFile = true;
    }

    partial void OnUseUploadedFileChanged(bool value)
    {
        if (value && UseExistingBatch)
            UseExistingBatch = false;
        else if (!value && !UseExistingBatch)
            UseExistingBatch = true;
    }

    [ObservableProperty]
    private BatchOption? _selectedBatch;

    [ObservableProperty]
    private string? _uploadedFilePath;

    [ObservableProperty]
    private decimal? _statementEndingBalance;

    // ─── 餘額對帳面板 ───
    [ObservableProperty]
    private string _balancePanelDisplay = string.Empty;

    public ReconciliationViewModel(
        IReconciliationService service,
        IReconciliationSessionRepository sessions,
        IAssetRepository assets,
        ITradeRepository trades,
        IReconciliationMatcher matcher,
        IImportBatchHistoryRepository? history = null,
        IImportFormatDetector? detector = null,
        ImportParserFactory? parserFactory = null,
        ILocalizationService? localization = null,
        ICurrencyService? currency = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(matcher);
        _service = service;
        _sessions = sessions;
        _assets = assets;
        _trades = trades;
        _matcher = matcher;
        _history = history;
        _detector = detector;
        _parserFactory = parserFactory;
        _localization = localization;
        _currency = currency;

        GroupedDiffs = CollectionViewSource.GetDefaultView(Diffs);
        GroupedDiffs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReconciliationDiffRowViewModel.KindDisplay)));

        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
    }

    public string SummaryDisplay
    {
        get
        {
            int pending = 0, resolved = 0;
            foreach (var d in Diffs)
            {
                if (d.IsPending) pending++; else resolved++;
            }
            return string.Format(
                GetString("Reconciliation.Summary", "待處理：{0} / 已處理：{1} / 總數：{2}"),
                pending, resolved, Diffs.Count);
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Sessions.Clear();
            foreach (var s in await _sessions.GetAllAsync().ConfigureAwait(true))
                Sessions.Add(s);

            AccountOptions.Clear();
            var assets = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(true);
            foreach (var item in assets.Where(a => a.IsActive))
                AccountOptions.Add(new CashAccountOption(item.Id, item.Name));

            BatchOptions.Clear();
            if (_history is not null)
            {
                var batches = await _history.GetRecentAsync(50).ConfigureAwait(true);
                foreach (var b in batches.Where(x => !x.IsRolledBack))
                    BatchOptions.Add(new BatchOption(b.Id, b.FileName, b.AppliedAt));
            }

            if (Sessions.Count > 0 && SelectedSession is null)
                SelectedSession = Sessions[0];
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedSessionChanged(ReconciliationSession? value)
    {
        _ = ReloadDiffsAsync(value, ++_reloadVersion);
    }

    private Task ReloadDiffsAsync()
        => ReloadDiffsAsync(SelectedSession, ++_reloadVersion);

    private async Task ReloadDiffsAsync(ReconciliationSession? session, int version)
    {
        if (session is null)
        {
            if (version != _reloadVersion)
                return;
            Diffs.Clear();
            BalancePanelDisplay = string.Empty;
            OnPropertyChanged(nameof(SummaryDisplay));
            return;
        }

        var diffs = await _sessions.GetDiffsAsync(session.Id).ConfigureAwait(true);
        var tradeLookup = await LoadTradeLookupAsync(session).ConfigureAwait(true);
        if (version != _reloadVersion || SelectedSession?.Id != session.Id)
            return;

        Diffs.Clear();
        foreach (var d in diffs)
        {
            tradeLookup.TryGetValue(d.TradeId ?? Guid.Empty, out var trade);
            var tradeAmount = trade is null ? (decimal?)null : _matcher.SignedAmount(trade);
            Diffs.Add(new ReconciliationDiffRowViewModel(d, trade, tradeAmount));
        }
        OnPropertyChanged(nameof(SummaryDisplay));
        await RecomputeBalancePanelAsync(session, version).ConfigureAwait(true);
    }

    private Task RecomputeBalancePanelAsync()
        => RecomputeBalancePanelAsync(SelectedSession, _reloadVersion);

    private async Task RecomputeBalancePanelAsync(ReconciliationSession? session, int version)
    {
        if (session is null) { BalancePanelDisplay = string.Empty; return; }
        try
        {
            var rows = await _sessions.GetStatementRowsAsync(session.Id).ConfigureAwait(true);
            var trades = await LoadTradesForSessionAsync(session).ConfigureAwait(true);
            if (version != _reloadVersion || SelectedSession?.Id != session.Id)
                return;

            var stmtSum = rows.Sum(r => _matcher.SignedAmount(r));
            var tradeSum = trades.Sum(t => _matcher.SignedAmount(t));
            var delta = stmtSum - tradeSum;
            var endingDisplay = session.StatementEndingBalance is { } eb
                ? FormatAmount(eb)
                : "—";

            BalancePanelDisplay = string.Format(
                GetString(
                    "Reconciliation.Balance.Summary",
                    "對帳單：{0} / 帳上交易：{1} / 差額：{2}\n對帳單期末餘額：{3}"),
                FormatAmount(stmtSum),
                FormatAmount(tradeSum),
                FormatSigned(delta),
                endingDisplay);
        }
        catch (Exception ex)
        {
            BalancePanelDisplay = ex.Message;
        }
    }

    private async Task<IReadOnlyList<Trade>> LoadTradesForSessionAsync(ReconciliationSession session)
    {
        var start = session.PeriodStart;
        var end = session.PeriodEnd;
        return (await _trades.GetByCashAccountAsync(session.AccountId).ConfigureAwait(true))
            .Where(t => DateOnly.FromDateTime(t.TradeDate) >= start
                     && DateOnly.FromDateTime(t.TradeDate) <= end)
            .ToList();
    }

    private async Task<Dictionary<Guid, Trade>> LoadTradeLookupAsync(ReconciliationSession session)
        => (await LoadTradesForSessionAsync(session).ConfigureAwait(true))
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First());

    [RelayCommand]
    public async Task RecomputeAsync()
    {
        if (SelectedSession is null) return;
        IsBusy = true;
        try
        {
            await _service.RecomputeAsync(SelectedSession.Id).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
            StatusMessage = GetString("Reconciliation.Status.Recomputed", "已重新比對。");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SignOffAsync()
    {
        if (SelectedSession is null) return;
        try
        {
            await _service.SignOffAsync(SelectedSession.Id, note: null).ConfigureAwait(true);
            StatusMessage = GetString("Reconciliation.Status.SignedOff", "已完成簽收。");
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public Task MarkResolvedAsync(ReconciliationDiffRowViewModel? row)
        => row is null
            ? Task.CompletedTask
            : ApplySimpleResolutionAsync(row.Id, ReconciliationDiffResolution.MarkedResolved);

    [RelayCommand]
    public Task IgnoreAsync(ReconciliationDiffRowViewModel? row)
        => row is null
            ? Task.CompletedTask
            : ApplySimpleResolutionAsync(row.Id, ReconciliationDiffResolution.Ignored);

    [RelayCommand]
    public Task DeleteTradeAsync(ReconciliationDiffRowViewModel? row)
        => row is null || row.Kind != ReconciliationDiffKind.Extra
            ? Task.CompletedTask
            : ApplySimpleResolutionAsync(row.Id, ReconciliationDiffResolution.Deleted);

    [RelayCommand]
    public async Task CreateTradeAsync(ReconciliationDiffRowViewModel? row)
    {
        if (row is null || row.Kind != ReconciliationDiffKind.Missing) return;
        if (SelectedSession is null) return;
        try
        {
            var options = new ImportApplyOptions(CashAccountId: SelectedSession.AccountId);
            await _service.ApplyResolutionAsync(
                row.Id, ReconciliationDiffResolution.Created, note: null,
                ImportSourceKind.BankStatement, options).ConfigureAwait(true);
            await _service.RecomputeAsync(SelectedSession.Id).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task OverwriteFromStatementAsync(ReconciliationDiffRowViewModel? row)
    {
        if (row is null || row.Kind != ReconciliationDiffKind.AmountMismatch) return;
        if (SelectedSession is null) return;
        try
        {
            var options = new ImportApplyOptions(CashAccountId: SelectedSession.AccountId);
            await _service.ApplyResolutionAsync(
                row.Id, ReconciliationDiffResolution.OverwrittenFromStatement, note: null,
                ImportSourceKind.BankStatement, options).ConfigureAwait(true);
            await _service.RecomputeAsync(SelectedSession.Id).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ApplySimpleResolutionAsync(Guid diffId, ReconciliationDiffResolution resolution)
    {
        try
        {
            await _service.ApplyResolutionAsync(diffId, resolution, note: null).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public void ToggleNewSessionPanel() => IsNewSessionPanelOpen = !IsNewSessionPanelOpen;

    [RelayCommand]
    public void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Statement files (*.csv;*.xlsx;*.xls)|*.csv;*.xlsx;*.xls|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            UploadedFilePath = dlg.FileName;
            UseExistingBatch = false;
        }
    }

    [RelayCommand]
    public async Task CreateSessionAsync()
    {
        if (NewSessionAccount is null)
        {
            StatusMessage = GetString("Reconciliation.Error.AccountRequired", "請先選擇帳戶。");
            return;
        }
        if (NewPeriodEnd < NewPeriodStart)
        {
            StatusMessage = GetString("Reconciliation.Error.PeriodInvalid", "結束日期必須晚於或等於開始日期。");
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<ImportPreviewRow> rows;
            Guid? sourceBatchId = null;

            if (UseExistingBatch)
            {
                if (SelectedBatch is null || _history is null)
                {
                    StatusMessage = GetString("Reconciliation.Error.BatchRequired", "請選擇既有匯入批次。");
                    return;
                }
                rows = await _history.GetPreviewRowsAsync(SelectedBatch.Id).ConfigureAwait(true);
                sourceBatchId = SelectedBatch.Id;
            }
            else
            {
                if (string.IsNullOrEmpty(UploadedFilePath) || _detector is null || _parserFactory is null)
                {
                    StatusMessage = GetString("Reconciliation.Error.FileOrServiceRequired", "請先選擇檔案，或確認匯入服務可用。");
                    return;
                }
                var fileType = ResolveFileType(UploadedFilePath);
                if (fileType is null)
                {
                    StatusMessage = GetString("Reconciliation.Error.UnsupportedFileType", "目前不支援這個檔案類型。");
                    return;
                }
                ImportFormat? format;
                await using (var detectStream = File.OpenRead(UploadedFilePath))
                {
                    format = await _detector.DetectAsync(Path.GetFileName(UploadedFilePath), detectStream)
                        .ConfigureAwait(true);
                }
                var parser = _parserFactory.Create(format ?? ImportFormat.Generic, fileType.Value);
                await using var parseStream = File.OpenRead(UploadedFilePath);
                rows = await parser.ParseAsync(parseStream).ConfigureAwait(true);
            }

            if (rows.Count == 0)
            {
                StatusMessage = GetString(
                    "Reconciliation.Error.NoStatementRows",
                    "No statement rows are available for this reconciliation source.");
                return;
            }

            var session = await _service.CreateAsync(
                NewSessionAccount.Id,
                DateOnly.FromDateTime(NewPeriodStart),
                DateOnly.FromDateTime(NewPeriodEnd),
                rows,
                sourceBatchId,
                note: null,
                statementEndingBalance: StatementEndingBalance).ConfigureAwait(true);

            Sessions.Insert(0, session);
            SelectedSession = session;
            IsNewSessionPanelOpen = false;
            UploadedFilePath = null;
            StatementEndingBalance = null;
            StatusMessage = string.Format(
                GetString("Reconciliation.Status.CreatedSession", "已建立對帳作業（{0} 列）。"),
                rows.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SummaryDisplay));
        _ = RecomputeBalancePanelAsync();
    }

    private void OnCurrencyChanged()
    {
        _ = RecomputeBalancePanelAsync();
    }

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? value.ToString("N2", CultureInfo.InvariantCulture);

    private string FormatSigned(decimal value) =>
        _currency?.FormatSigned(value) ?? value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private string GetString(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    private static ImportFileType? ResolveFileType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ImportFileType.Csv,
            ".xlsx" or ".xls" => ImportFileType.Excel,
            _ => null,
        };
}

public sealed record BatchOption(Guid Id, string FileName, DateTimeOffset AppliedAt)
{
    public string Display => $"{FileName} ({AppliedAt:yyyy-MM-dd HH:mm})";
}
