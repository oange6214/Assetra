using Assetra.Core.Models.Fire;

namespace Assetra.Core.Interfaces.Fire;

public interface IFireDrawdownService
{
    FireDrawdownProjection ProjectDrawdown(
        decimal startingBalance,
        decimal annualRetirementExpenses,
        decimal expectedAnnualReturn,
        int currentAge,
        int lifeExpectancyAge);
}
