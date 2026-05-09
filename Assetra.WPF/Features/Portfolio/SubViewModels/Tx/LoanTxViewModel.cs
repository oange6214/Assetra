using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — fourth child VM split off from <c>TransactionDialogViewModel</c>.
/// Owns the **loan transaction state cluster** for both LoanBorrow and LoanRepay:
/// <list type="bullet">
///   <item><see cref="Label"/>: identifies which loan account (label = account key)</item>
///   <item>LoanRepay split: <see cref="Principal"/> + <see cref="InterestPaid"/>
///         + <see cref="PrincipalError"/> + <see cref="InterestPaidError"/></item>
///   <item>LoanBorrow amortization metadata: <see cref="Rate"/> + <see cref="TermMonths"/>
///         + <see cref="StartDate"/> + <see cref="RateError"/> + <see cref="TermMonthsError"/></item>
/// </list>
///
/// <para>
/// Parent dialog VM listens to <see cref="ObservableObject.PropertyChanged"/> for
/// validation + AutoFillLoanRepay + impact-preview side effects.
/// </para>
/// </summary>
public sealed partial class LoanTxViewModel : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;

    // LoanRepay 拆分
    [ObservableProperty] private string _principal = string.Empty;
    [ObservableProperty] private string _principalError = string.Empty;
    [ObservableProperty] private string _interestPaid = string.Empty;
    [ObservableProperty] private string _interestPaidError = string.Empty;

    // LoanBorrow amortization (optional — fills attached schedule when present)
    [ObservableProperty] private string _rate = string.Empty;
    [ObservableProperty] private string _rateError = string.Empty;
    [ObservableProperty] private string _termMonths = string.Empty;
    [ObservableProperty] private string _termMonthsError = string.Empty;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;

    public void Reset()
    {
        Label = string.Empty;
        Principal = string.Empty;
        PrincipalError = string.Empty;
        InterestPaid = string.Empty;
        InterestPaidError = string.Empty;
        Rate = string.Empty;
        RateError = string.Empty;
        TermMonths = string.Empty;
        TermMonthsError = string.Empty;
        StartDate = DateTime.Today;
    }
}
