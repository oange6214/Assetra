using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Dependencies passed to <see cref="LoanDialogViewModel"/> at construction.
/// </summary>
internal sealed record LoanDialogDependencies(
    ILoanPaymentWorkflowService LoanPayment,
    ILoanScheduleService? LoanSchedule,
    Func<LiabilityRowViewModel?> GetSelectedLiabilityRow,
    Func<Guid?> GetTxCashAccountId,
    Func<Task> LoadTradesAsync,
    Func<Task> ReloadAccountBalancesAsync,
    Action RebuildTotals);

/// <summary>
/// Owns the loan-schedule confirm-payment command and schedule-loading logic.
/// Raised <see cref="LoanChanged"/> after a successful payment so the parent
/// <see cref="PortfolioViewModel"/> can reload trades, balances, and totals.
/// </summary>
public partial class LoanDialogViewModel : ObservableObject
{
    private readonly ILoanPaymentWorkflowService _loanPayment;
    private readonly ILoanScheduleService? _loanScheduleService;
    private readonly Func<LiabilityRowViewModel?> _getSelectedLiabilityRow;
    private readonly Func<Guid?> _getTxCashAccountId;
    private readonly Func<Task> _loadTradesAsync;
    private readonly Func<Task> _reloadAccountBalancesAsync;
    private readonly Action _rebuildTotals;

    /// <summary>
    /// Raised after a successful loan payment so the parent can reload trades,
    /// account balances, and totals.
    /// </summary>
    public event EventHandler? LoanChanged;

    internal LoanDialogViewModel(LoanDialogDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _loanPayment = deps.LoanPayment;
        _loanScheduleService = deps.LoanSchedule;
        _getSelectedLiabilityRow = deps.GetSelectedLiabilityRow;
        _getTxCashAccountId = deps.GetTxCashAccountId;
        _loadTradesAsync = deps.LoadTradesAsync;
        _reloadAccountBalancesAsync = deps.ReloadAccountBalancesAsync;
        _rebuildTotals = deps.RebuildTotals;
    }

    // ── Commands ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ConfirmLoanPayment(LoanScheduleRowViewModel? entry)
    {
        if (entry is null || _getSelectedLiabilityRow() is not { } row || _loanScheduleService is null)
            return;
        if (entry.IsPaid)
            return;

        var cashAccId = _getTxCashAccountId();
        var result = await _loanPayment.RecordAsync(new LoanPaymentRequest(
            new LoanScheduleEntry(
                entry.Id,
                row.AssetId ?? Guid.Empty,
                entry.Period,
                entry.DueDate,
                entry.TotalAmount,
                entry.PrincipalAmount,
                entry.InterestAmount,
                entry.Remaining,
                entry.IsPaid,
                entry.PaidAt,
                entry.TradeId),
            row.Label,
            cashAccId,
            DateTime.UtcNow));
        entry.IsPaid = true;
        entry.PaidAt = result.PaidAt;
        entry.TradeId = result.RepayTrade.Id;
        row.RefreshScheduleSummary();

        LoanChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Schedule loading (called by parent when a liability row is selected) ──────────

    /// <summary>Loads the amortization schedule for a loan liability row.</summary>
    public async Task LoadLoanScheduleAsync(LiabilityRowViewModel row)
    {
        if (!row.IsLoan || row.AssetId is null || _loanScheduleService is null)
            return;
        var entries = await _loanScheduleService.GetScheduleByAssetAsync(row.AssetId.Value).ConfigureAwait(true);
        row.ScheduleEntries.Clear();
        foreach (var e in entries)
            row.ScheduleEntries.Add(new LoanScheduleRowViewModel(e));
        row.IsScheduleLoaded = true;
        row.RefreshScheduleSummary();
    }
}
