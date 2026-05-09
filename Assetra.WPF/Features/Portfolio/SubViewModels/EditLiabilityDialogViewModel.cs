using Assetra.Application.Portfolio.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Self-contained edit dialog for an existing <see cref="LiabilityRowViewModel"/>.
///
/// <para>
/// Lifecycle: parent calls <see cref="Open"/> with the selected row to populate
/// the form; the user clicks Save → <see cref="LiabilityUpdated"/> fires →
/// parent reloads liabilities and closes the dialog (driven by
/// <see cref="IsOpen"/>). Cancel just sets <see cref="IsOpen"/>=false.
/// </para>
///
/// <para>
/// The Loan rate/term recompute path is opt-in via <see cref="RecomputeSchedule"/>
/// + a confirmation step. Already-paid periods are preserved verbatim by the
/// underlying <c>ILoanScheduleRecomputeService</c>.
/// </para>
/// </summary>
public sealed partial class EditLiabilityDialogViewModel : ObservableObject
{
    private readonly ILiabilityMutationWorkflowService _liability;
    private readonly Infrastructure.ISnackbarService? _snackbar;
    private LiabilityRowViewModel? _editingRow;

    /// <summary>Fired after <see cref="SaveAsync"/> succeeds. Parent reloads + closes.</summary>
    public event EventHandler? LiabilityUpdated;

    public EditLiabilityDialogViewModel(
        ILiabilityMutationWorkflowService liability,
        Infrastructure.ISnackbarService? snackbar = null)
    {
        _liability = liability ?? throw new ArgumentNullException(nameof(liability));
        _snackbar = snackbar;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoanEdit))]
    [NotifyPropertyChangedFor(nameof(IsCreditCardEdit))]
    private bool _isOpen;

    /// <summary>True while the editing target is a loan; drives loan-only field visibility.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoanEdit))]
    private bool _editingLoan;

    /// <summary>True while the editing target is a credit card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreditCardEdit))]
    private bool _editingCreditCard;

    public bool IsLoanEdit => IsOpen && EditingLoan;
    public bool IsCreditCardEdit => IsOpen && EditingCreditCard;

    // ── Common fields ─────────────────────────────────────────────────────
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _issuerName = string.Empty;
    [ObservableProperty] private string _subtype = string.Empty;

    // ── Loan-only ─────────────────────────────────────────────────────────
    /// <summary>Annual rate as a percentage string (e.g. "2.5" for 2.5%).</summary>
    [ObservableProperty] private string _annualRatePercent = string.Empty;
    [ObservableProperty] private string _termMonths = string.Empty;
    [ObservableProperty] private string _handlingFee = string.Empty;
    /// <summary>When true, Loan rate/term changes trigger schedule recompute on save.</summary>
    [ObservableProperty] private bool _recomputeSchedule = true;

    // ── Credit card-only ──────────────────────────────────────────────────
    [ObservableProperty] private string _creditLimit = string.Empty;
    [ObservableProperty] private string _billingDay = string.Empty;
    [ObservableProperty] private string _dueDay = string.Empty;

    /// <summary>Last error from validation/save shown inline in the dialog footer.</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>
    /// Snapshot of original principal (for loans) — needed by the recompute path.
    /// Captured at <see cref="Open"/> time from the row's
    /// <see cref="LiabilityRowViewModel.OriginalAmount"/>.
    /// </summary>
    private decimal _capturedOriginalPrincipal;

    /// <summary>
    /// Detect whether rate/term differs from initial open snapshot — used to
    /// decide if the "重算攤還表" warning needs to be shown.
    /// </summary>
    private decimal _initialAnnualRate;
    private int _initialTermMonths;

    public bool LoanRateOrTermChanged
    {
        get
        {
            if (!EditingLoan) return false;
            if (!decimal.TryParse(AnnualRatePercent, out var rate)) return false;
            if (!int.TryParse(TermMonths, out var term)) return false;
            return rate / 100m != _initialAnnualRate || term != _initialTermMonths;
        }
    }

    /// <summary>
    /// Populates the form from an existing row and shows the dialog. Call from
    /// the parent VM's "OpenEditLiability" command.
    /// </summary>
    public void Open(LiabilityRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _editingRow = row;
        EditingLoan = row.IsLoan;
        EditingCreditCard = row.IsCreditCard;
        Name = row.Name;
        IssuerName = row.IssuerName ?? string.Empty;
        Subtype = string.Empty; // Subtype isn't surfaced on the row VM yet; user can fill if needed.

        if (row.IsLoan)
        {
            _capturedOriginalPrincipal = row.OriginalAmount;
            _initialAnnualRate = row.LoanAnnualRate ?? 0m;
            _initialTermMonths = row.LoanTermMonths ?? 0;
            AnnualRatePercent = row.LoanAnnualRate.HasValue ? (row.LoanAnnualRate.Value * 100m).ToString("0.####") : string.Empty;
            TermMonths = row.LoanTermMonths?.ToString() ?? string.Empty;
            HandlingFee = row.LoanHandlingFee?.ToString("0.##") ?? string.Empty;
        }
        else
        {
            _capturedOriginalPrincipal = 0m;
            _initialAnnualRate = 0m;
            _initialTermMonths = 0;
            AnnualRatePercent = string.Empty;
            TermMonths = string.Empty;
            HandlingFee = string.Empty;
        }

        if (row.IsCreditCard)
        {
            CreditLimit = row.CreditLimit?.ToString("0.##") ?? string.Empty;
            BillingDay = row.BillingDay?.ToString() ?? string.Empty;
            DueDay = row.DueDay?.ToString() ?? string.Empty;
        }
        else
        {
            CreditLimit = string.Empty;
            BillingDay = string.Empty;
            DueDay = string.Empty;
        }

        ErrorMessage = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        ErrorMessage = string.Empty;
        _editingRow = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_editingRow is null) return;
        if (_editingRow.AssetId is not { } assetId)
        {
            ErrorMessage = "此負債沒有對應的資產 (legacy label-only)，無法編輯。";
            return;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "名稱不能為空。";
            return;
        }

        // Parse numeric fields up front; surface user-friendly errors.
        decimal? newAnnualRate = null;
        int? newTermMonths = null;
        decimal? newHandlingFee = null;
        if (EditingLoan)
        {
            if (!decimal.TryParse(AnnualRatePercent, out var ratePct) || ratePct < 0)
            {
                ErrorMessage = "年利率格式錯誤（應為數字 %，例如 2.5）。";
                return;
            }
            newAnnualRate = ratePct / 100m;

            if (!int.TryParse(TermMonths, out var term) || term <= 0)
            {
                ErrorMessage = "期數格式錯誤（必須為正整數月數）。";
                return;
            }
            newTermMonths = term;

            if (!string.IsNullOrWhiteSpace(HandlingFee))
            {
                if (!decimal.TryParse(HandlingFee, out var fee) || fee < 0)
                {
                    ErrorMessage = "手續費格式錯誤。";
                    return;
                }
                newHandlingFee = fee;
            }
        }

        decimal? newCreditLimit = null;
        int? newBillingDay = null;
        int? newDueDay = null;
        if (EditingCreditCard)
        {
            if (!string.IsNullOrWhiteSpace(CreditLimit))
            {
                if (!decimal.TryParse(CreditLimit, out var limit) || limit < 0)
                {
                    ErrorMessage = "信用額度格式錯誤。";
                    return;
                }
                newCreditLimit = limit;
            }
            if (!string.IsNullOrWhiteSpace(BillingDay))
            {
                if (!int.TryParse(BillingDay, out var bd) || bd < 1 || bd > 31)
                {
                    ErrorMessage = "帳單日必須是 1–31。";
                    return;
                }
                newBillingDay = bd;
            }
            if (!string.IsNullOrWhiteSpace(DueDay))
            {
                if (!int.TryParse(DueDay, out var dd) || dd < 1 || dd > 31)
                {
                    ErrorMessage = "繳款日必須是 1–31。";
                    return;
                }
                newDueDay = dd;
            }
        }

        try
        {
            var request = new LiabilityUpdateRequest(
                AssetId:          assetId,
                NewName:          Name.Trim(),
                NewIssuerName:    string.IsNullOrWhiteSpace(IssuerName) ? null : IssuerName.Trim(),
                NewSubtype:       string.IsNullOrWhiteSpace(Subtype) ? null : Subtype.Trim(),
                NewAnnualRate:    newAnnualRate,
                NewTermMonths:    newTermMonths,
                NewHandlingFee:   newHandlingFee,
                RecomputeSchedule: EditingLoan && RecomputeSchedule && LoanRateOrTermChanged,
                OriginalPrincipal: EditingLoan ? _capturedOriginalPrincipal : null,
                NewCreditLimit:   newCreditLimit,
                NewBillingDay:    newBillingDay,
                NewDueDay:        newDueDay);

            var result = await _liability.UpdateAsync(request).ConfigureAwait(true);

            if (result.ScheduleRecomputed)
            {
                _snackbar?.Success(
                    $"已更新並重算攤還表：保留已付 {result.PreservedPaidCount} 期，重生成未付 {result.RegeneratedUnpaidCount} 期。");
            }
            else
            {
                _snackbar?.Success("負債資料已更新。");
            }

            IsOpen = false;
            ErrorMessage = string.Empty;
            _editingRow = null;
            LiabilityUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = "更新失敗：" + ex.Message;
        }
    }
}
