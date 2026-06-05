using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class CalculatorsViewModel : ObservableObject
{
    public LoanCalcViewModel Loan { get; }
    public RentVsBuyCalcViewModel RentVsBuy { get; }

    public CalculatorsViewModel(
        LoanCalcViewModel loan,
        RentVsBuyCalcViewModel rentVsBuy)
    {
        Loan = loan;
        RentVsBuy = rentVsBuy;
    }
}
