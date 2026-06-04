using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Calculators;

public sealed partial class CalculatorsViewModel : ObservableObject
{
    public LoanCalcViewModel Loan { get; }
    public LatteFactorCalcViewModel Latte { get; }
    public RuleOf72CalcViewModel Rule72 { get; }
    public RentVsBuyCalcViewModel RentVsBuy { get; }

    public CalculatorsViewModel(
        LoanCalcViewModel loan,
        LatteFactorCalcViewModel latte,
        RuleOf72CalcViewModel rule72,
        RentVsBuyCalcViewModel rentVsBuy)
    {
        Loan = loan;
        Latte = latte;
        Rule72 = rule72;
        RentVsBuy = rentVsBuy;
    }
}
