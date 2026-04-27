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

    public bool UseUploadedFile
    {
        get => !UseExistingBatch;
        set
        {
            if (UseExistingBatch == !value) return;
            UseExistingBatch = !value;
        }
    }

    partial void OnUseExistingBatchChanged(bool value)
    {
        OnPropertyChanged(nameof(UseUploadedFile));
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
        ImportParserFactory? parserFactory = null)
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

        GroupedDiffs = CollectionViewSource.GetDefaultView(Diffs);
        GroupedDiffs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReconciliationDiffRowViewModel.KindDisplay)));
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
                CultureInfo.InvariantCulture,
                "Pending: {0} / Resolved: {1} / Total: {2}",
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
        _ = ReloadDiffsAsync();
    }

    private async Task ReloadDiffsAsync()
    {
        Diffs.Clear();
        if (SelectedSession is null)
        {
            BalancePanelDisplay = string.Empty;
            OnPropertyChanged(nameof(SummaryDisplay));
            return;
        }
        var diffs = await _sessions.GetDiffsAsync(SelectedSession.Id).ConfigureAwait(true);
        foreach (var d in diffs)
            Diffs.Add(new ReconciliationDiffRowViewModel(d));
        OnPropertyChanged(nameof(SummaryDisplay));
        await RecomputeBalancePanelAsync().ConfigureAwait(true);
    }

    private async Task RecomputeBalancePanelAsync()
    {
        if (SelectedSession is null) { BalancePanelDisplay = string.Empty; return; }
        try
        {
            var rows = await _sessions.GetStatementRowsAsync(SelectedSession.Id).ConfigureAwait(true);
            var trades = (await _trades.GetByCashAccountAsync(SelectedSession.AccountId).ConfigureAwait(true))
                .Where(t => DateOnly.FromDateTime(t.TradeDate) >= SelectedSession.PeriodStart
                         && DateOnly.FromDateTime(t.TradeDate) <= SelectedSession.PeriodEnd)
                .ToList();

            var stmtSum = rows.Sum(r => _matcher.SignedAmount(r));
            var tradeSum = trades.Sum(t => _matcher.SignedAmount(t));
            var delta = stmtSum - tradeSum;
            var endingDisplay = SelectedSession.StatementEndingBalance is { } eb
                ? eb.ToString("N2", CultureInfo.InvariantCulture)
                : "—";

            BalancePanelDisplay = string.Format(
                CultureInfo.InvariantCulture,
                "Statement: {0:N2} / Trades: {1:N2} / Δ: {2:N2}\nEnding (statement): {3}",
                stmtSum, tradeSum, delta, endingDisplay);
        }
        catch (Exception ex)
        {
            BalancePanelDisplay = ex.Message;
        }
    }

    [RelayCommand]
    public async Task RecomputeAsync()
    {
        if (SelectedSession is null) return;
        IsBusy = true;
        try
        {
            await _service.RecomputeAsync(SelectedSession.Id).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
            StatusMessage = "Recomputed.";
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
            StatusMessage = "Signed off.";
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
            StatusMessage = "Pick an account first.";
            return;
        }
        if (NewPeriodEnd < NewPeriodStart)
        {
            StatusMessage = "Period end must be >= start.";
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
                    StatusMessage = "Pick an existing batch.";
                    return;
                }
                rows = await _history.GetPreviewRowsAsync(SelectedBatch.Id).ConfigureAwait(true);
                sourceBatchId = SelectedBatch.Id;
            }
            else
            {
                if (string.IsNullOrEmpty(UploadedFilePath) || _detector is null || _parserFactory is null)
                {
                    StatusMessage = "Pick a file or import services not available.";
                    return;
                }
                var fileType = ResolveFileType(UploadedFilePath);
                if (fileType is null)
                {
                    StatusMessage = "Unsupported file type.";
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
            StatusMessage = $"Created session ({rows.Count} rows).";
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
