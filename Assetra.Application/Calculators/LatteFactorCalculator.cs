using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class LatteFactorCalculator
{
    public LatteFactorResult Calculate(LatteFactorInputs i)
    {
        if (i.AmountPerSpend < 0) throw new ArgumentOutOfRangeException(nameof(i.AmountPerSpend));
        if (i.Years <= 0) throw new ArgumentOutOfRangeException(nameof(i.Years));
        var monthly = i.Frequency switch
        {
            LatteFrequency.Daily => i.AmountPerSpend * 365m / 12m,
            LatteFrequency.Weekly => i.AmountPerSpend * 52m / 12m,
            _ => i.AmountPerSpend,
        };
        var r = i.AnnualReturn / 12m;
        var n = i.Years * 12;
        decimal fv = r == 0m ? monthly * n : monthly * (LoanAmortizationService.Pow(1m + r, n) - 1m) / r;
        var contributed = monthly * n;
        return new(decimal.Round(contributed, 0), decimal.Round(fv, 0), decimal.Round(fv - contributed, 0));
    }
}
