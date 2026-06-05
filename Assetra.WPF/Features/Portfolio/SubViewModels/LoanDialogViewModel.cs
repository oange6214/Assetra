using Assetra.Application.Loans.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Dependencies passed to <see cref="LoanDialogViewModel"/> at construction.
/// </summary>
internal sealed record LoanDialogDependencies(
    ILoanScheduleService? LoanSchedule,
    Func<LiabilityRowViewModel?> GetSelectedLiabilityRow,
    Action<LiabilityRowViewModel, LoanScheduleRowViewModel> OpenLoanRepaymentTrade);

/// <summary>
/// Owns the loan-schedule repayment command and schedule-loading logic.
/// Schedule rows are proposals; the transaction journal is the source of truth.
/// </summary>
public partial class LoanDialogViewModel : ObservableObject
{
    private readonly ILoanScheduleService? _loanScheduleService;
    private readonly Func<LiabilityRowViewModel?> _getSelectedLiabilityRow;
    private readonly Action<LiabilityRowViewModel, LoanScheduleRowViewModel> _openLoanRepaymentTrade;

    internal LoanDialogViewModel(LoanDialogDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _loanScheduleService = deps.LoanSchedule;
        _getSelectedLiabilityRow = deps.GetSelectedLiabilityRow;
        _openLoanRepaymentTrade = deps.OpenLoanRepaymentTrade;
    }

    // ── Commands ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmLoanPayment(LoanScheduleRowViewModel? entry)
    {
        if (entry is null || entry.IsPaid || _getSelectedLiabilityRow() is not { } row)
            return;
        _openLoanRepaymentTrade(row, entry);
    }

    // ── Schedule loading (called by parent when a liability row is selected) ──────────

    /// <summary>Loads the amortization schedule for a loan liability row.</summary>
    public async Task LoadLoanScheduleAsync(LiabilityRowViewModel row)
    {
        if (!row.IsLoan || row.AssetId is null || _loanScheduleService is null)
            return;
        var entries = await _loanScheduleService.GetScheduleByAssetAsync(row.AssetId.Value).ConfigureAwait(true);
        row.ReplaceSchedule(entries.Select(e => new LoanScheduleRowViewModel(e)));
        row.IsScheduleLoaded = true;
        row.RefreshScheduleSummary();
    }
}
