using System.Collections.ObjectModel;
using System.IO;
using Assetra.Application.Import;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Assetra.WPF.Features.Import;

/// <summary>
/// 匯入流程 orchestrator：上傳 → 偵測格式 → 解析預覽 → 衝突偵測 → Apply。
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private static readonly IReadOnlyList<(string Label, string Pattern)> FileFilters =
    [
        ("CSV / Excel", "*.csv;*.xlsx;*.xls"),
        ("CSV", "*.csv"),
        ("Excel", "*.xlsx;*.xls"),
    ];

    private const int HistoryListLimit = 20;

    private readonly IImportFormatDetector _detector;
    private readonly ImportParserFactory _parserFactory;
    private readonly IImportConflictDetector _conflictDetector;
    private readonly IImportApplyService _applyService;
    private readonly IImportBatchHistoryRepository _historyRepo;
    private readonly IImportRollbackService _rollbackService;
    private readonly IAssetRepository _assets;
    private readonly ISnackbarService _snackbar;
    private readonly ILocalizationService? _localization;

    private ImportBatch? _currentBatch;

    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];
    public ObservableCollection<CashAccountOption> CashAccountOptions { get; } = [];
    public ObservableCollection<ImportHistoryRowViewModel> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyPropertyChangedFor(nameof(SelectedFileName))]
    private string? _selectedFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedFormat))]
    [NotifyPropertyChangedFor(nameof(DetectedFormatLabel))]
    private ImportFormat? _detectedFormat;

    [ObservableProperty] private ImportFileType? _detectedFileType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isWorking;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private CashAccountOption? _selectedCashAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    [NotifyPropertyChangedFor(nameof(HasNoRows))]
    [NotifyPropertyChangedFor(nameof(ConflictCount))]
    [NotifyPropertyChangedFor(nameof(NewRowCount))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _rowCount;

    [ObservableProperty] private string? _applySummary;

    public bool HasFile => !string.IsNullOrEmpty(SelectedFilePath);
    public string SelectedFileName => string.IsNullOrEmpty(SelectedFilePath)
        ? string.Empty
        : Path.GetFileName(SelectedFilePath);
    public bool HasDetectedFormat => DetectedFormat.HasValue;
    public string DetectedFormatLabel
    {
        get
        {
            if (!DetectedFormat.HasValue) return string.Empty;
            var key = $"Import.Format.{DetectedFormat.Value}";
            return _localization?.Get(key, DetectedFormat.Value.ToString()) ?? DetectedFormat.Value.ToString();
        }
    }
    public bool IsBusy => IsWorking;
    public bool HasRows => RowCount > 0;
    public bool HasNoRows => !HasRows;
    public int ConflictCount => _currentBatch?.ConflictCount ?? 0;
    public int NewRowCount => _currentBatch?.NewRowCount ?? 0;
    public bool HasHistory => History.Count > 0;

    public ImportViewModel(
        IImportFormatDetector detector,
        ImportParserFactory parserFactory,
        IImportConflictDetector conflictDetector,
        IImportApplyService applyService,
        IImportBatchHistoryRepository historyRepo,
        IImportRollbackService rollbackService,
        IAssetRepository assets,
        ISnackbarService snackbar,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(parserFactory);
        ArgumentNullException.ThrowIfNull(conflictDetector);
        ArgumentNullException.ThrowIfNull(applyService);
        ArgumentNullException.ThrowIfNull(historyRepo);
        ArgumentNullException.ThrowIfNull(rollbackService);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(snackbar);

        _detector = detector;
        _parserFactory = parserFactory;
        _conflictDetector = conflictDetector;
        _applyService = applyService;
        _historyRepo = historyRepo;
        _rollbackService = rollbackService;
        _assets = assets;
        _snackbar = snackbar;
        _localization = localization;

        History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var items = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(true);
            CashAccountOptions.Clear();
            foreach (var item in items.Where(i => i.IsActive))
                CashAccountOptions.Add(new CashAccountOption(item.Id, item.Name));
            if (SelectedCashAccount is null && CashAccountOptions.Count > 0)
                SelectedCashAccount = CashAccountOptions[0];

            await RefreshHistoryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        var list = await _historyRepo.GetRecentAsync(HistoryListLimit).ConfigureAwait(true);
        History.Clear();
        foreach (var h in list)
            History.Add(new ImportHistoryRowViewModel(h));
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private async Task RollbackAsync(ImportHistoryRowViewModel? row)
    {
        if (row is null || row.IsRolledBack || IsWorking) return;

        IsWorking = true;
        try
        {
            var result = await _rollbackService.RollbackAsync(row.Id).ConfigureAwait(true);
            if (result.IsFullyReverted)
            {
                row.MarkRolledBack();
                _snackbar.Success(string.Format(
                    L("Import.Rollback.Success", "Rolled back {0}: -{1} reverted, {2} restored."),
                    row.FileName, result.Reverted, result.Restored));
            }
            else
            {
                var reason = result.Failures.Count > 0 ? result.Failures[0].Reason : "unknown";
                _snackbar.Warning(string.Format(
                    L("Import.Rollback.PartialFailure",
                        "Rollback partially failed ({0} failures). First reason: {1}"),
                    result.Failures.Count, reason));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _snackbar.Error(ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Import.Pick.Title", "Choose statement file"),
            Filter = string.Join('|', FileFilters.Select(f => $"{f.Label}|{f.Pattern}")),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            await ProcessFileAsync(dialog.FileName).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task DropFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await ProcessFileAsync(path).ConfigureAwait(true);
    }

    private async Task ProcessFileAsync(string path)
    {
        if (IsWorking) return;
        IsWorking = true;
        ErrorMessage = null;
        StatusMessage = null;
        ApplySummary = null;

        try
        {
            SelectedFilePath = path;
            var fileType = ResolveFileType(path);
            if (fileType is null)
            {
                ErrorMessage = L("Import.Error.UnsupportedExtension",
                    "Only .csv, .xlsx and .xls files are supported.");
                ResetBatch();
                return;
            }
            DetectedFileType = fileType;

            ImportFormat? format;
            await using (var detectStream = File.OpenRead(path))
            {
                format = await _detector.DetectAsync(Path.GetFileName(path), detectStream)
                    .ConfigureAwait(true);
            }
            DetectedFormat = format ?? ImportFormat.Generic;

            var parser = _parserFactory.Create(DetectedFormat.Value, fileType.Value);
            IReadOnlyList<ImportPreviewRow> rows;
            await using (var parseStream = File.OpenRead(path))
            {
                rows = await parser.ParseAsync(parseStream).ConfigureAwait(true);
            }

            var batch = new ImportBatch(
                Guid.NewGuid(),
                Path.GetFileName(path),
                fileType.Value,
                DetectedFormat.Value,
                DateTimeOffset.UtcNow,
                rows,
                Array.Empty<ImportConflict>());

            var withConflicts = await _conflictDetector.DetectAsync(batch).ConfigureAwait(true);
            BindBatch(withConflicts);
            StatusMessage = string.Format(
                L("Import.Status.ParsedRows", "Parsed {0} rows ({1} conflicts)."),
                withConflicts.RowCount, withConflicts.ConflictCount);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ResetBatch();
        }
        finally
        {
            IsWorking = false;
        }
    }

    private void BindBatch(ImportBatch batch)
    {
        _currentBatch = batch;
        Rows.Clear();
        var byIndex = batch.Conflicts.ToDictionary(c => c.Row.RowIndex);
        foreach (var row in batch.Rows)
        {
            if (byIndex.TryGetValue(row.RowIndex, out var conflict))
                Rows.Add(new ImportRowViewModel(row, conflict));
            else
                Rows.Add(new ImportRowViewModel(row));
        }
        RowCount = Rows.Count;
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(NewRowCount));
    }

    private void ResetBatch()
    {
        _currentBatch = null;
        Rows.Clear();
        RowCount = 0;
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(NewRowCount));
    }

    [RelayCommand]
    private void Clear()
    {
        SelectedFilePath = null;
        DetectedFormat = null;
        DetectedFileType = null;
        ErrorMessage = null;
        StatusMessage = null;
        ApplySummary = null;
        ResetBatch();
    }

    private bool CanApply() =>
        !IsWorking && _currentBatch is not null && RowCount > 0 && SelectedCashAccount is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_currentBatch is null || SelectedCashAccount is null) return;

        IsWorking = true;
        ErrorMessage = null;
        try
        {
            // Sync each row's resolution back into the batch's conflicts list
            var resolutions = Rows.Where(r => r.HasConflict)
                .ToDictionary(r => r.RowIndex, r => r.Resolution);

            var updatedConflicts = _currentBatch.Conflicts
                .Select(c => resolutions.TryGetValue(c.Row.RowIndex, out var res)
                    ? c with { Resolution = res }
                    : c)
                .ToArray();

            var batch = _currentBatch with { Conflicts = updatedConflicts };

            var options = new ImportApplyOptions(CashAccountId: SelectedCashAccount.Id);
            var result = await _applyService.ApplyAsync(batch, options).ConfigureAwait(true);

            ApplySummary = string.Format(
                L("Import.Apply.Summary",
                    "Applied {0} new, overwrote {1}, skipped {2}."),
                result.RowsApplied, result.RowsOverwritten, result.RowsSkipped);
            _snackbar.Success(ApplySummary);

            if (result.Warnings.Count > 0)
                _snackbar.Warning(string.Join(' ', result.Warnings));

            Clear();
            await RefreshHistoryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _snackbar.Error(ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private static ImportFileType? ResolveFileType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ImportFileType.Csv,
            ".xlsx" or ".xls" => ImportFileType.Excel,
            _ => null,
        };

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
