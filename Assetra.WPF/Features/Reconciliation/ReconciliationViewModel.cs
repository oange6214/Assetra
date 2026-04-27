using System.Collections.ObjectModel;
using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Reconciliation;
using Assetra.WPF.Features.Import;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Reconciliation;

/// <summary>
/// 對帳工作台 ViewModel：列出歷史 sessions、檢視選定 session 的 diffs、執行解決動作。
/// 本 v0.9.0 釋出版聚焦於 MVP 動作集（Mark resolved / Ignore / Delete trade）；
/// Created / OverwrittenFromStatement 需要 ImportRowMapper 整合，於下一輪補上。
/// </summary>
public partial class ReconciliationViewModel : ObservableObject
{
    private readonly IReconciliationService _service;
    private readonly IReconciliationSessionRepository _sessions;
    private readonly IAssetRepository _assets;

    public ObservableCollection<ReconciliationSession> Sessions { get; } = new();
    public ObservableCollection<ReconciliationDiffRowViewModel> Diffs { get; } = new();
    public ObservableCollection<CashAccountOption> AccountOptions { get; } = new();

    [ObservableProperty]
    private ReconciliationSession? _selectedSession;

    [ObservableProperty]
    private CashAccountOption? _newSessionAccount;

    [ObservableProperty]
    private DateTime _newPeriodStart = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _newPeriodEnd = DateTime.Today;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    public ReconciliationViewModel(
        IReconciliationService service,
        IReconciliationSessionRepository sessions,
        IAssetRepository assets)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(assets);
        _service = service;
        _sessions = sessions;
        _assets = assets;
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
        if (SelectedSession is null) return;
        var diffs = await _sessions.GetDiffsAsync(SelectedSession.Id).ConfigureAwait(true);
        foreach (var d in diffs)
            Diffs.Add(new ReconciliationDiffRowViewModel(d));
        OnPropertyChanged(nameof(SummaryDisplay));
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
    public async Task ApplyResolutionAsync((Guid diffId, ReconciliationDiffResolution resolution) args)
    {
        try
        {
            await _service.ApplyResolutionAsync(args.diffId, args.resolution, note: null).ConfigureAwait(true);
            await ReloadDiffsAsync().ConfigureAwait(true);
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
            : ApplyResolutionAsync((row.Id, ReconciliationDiffResolution.MarkedResolved));

    [RelayCommand]
    public Task IgnoreAsync(ReconciliationDiffRowViewModel? row)
        => row is null
            ? Task.CompletedTask
            : ApplyResolutionAsync((row.Id, ReconciliationDiffResolution.Ignored));

    [RelayCommand]
    public Task DeleteTradeAsync(ReconciliationDiffRowViewModel? row)
        => row is null || row.Kind != ReconciliationDiffKind.Extra
            ? Task.CompletedTask
            : ApplyResolutionAsync((row.Id, ReconciliationDiffResolution.Deleted));
}

