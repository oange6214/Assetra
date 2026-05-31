using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models.Fire;

namespace Assetra.Application.Fire;

public sealed class FireDrawdownService : IFireDrawdownService
{
    public FireDrawdownProjection ProjectDrawdown(
        decimal startingBalance,
        decimal annualRetirementExpenses,
        decimal expectedAnnualReturn,
        int currentAge,
        int lifeExpectancyAge)
    {
        if (startingBalance < 0m)
            throw new ArgumentOutOfRangeException(nameof(startingBalance), "Starting balance cannot be negative.");
        if (annualRetirementExpenses <= 0m)
            throw new ArgumentOutOfRangeException(nameof(annualRetirementExpenses), "Annual retirement expenses must be positive.");
        if (expectedAnnualReturn <= -1m)
            throw new ArgumentOutOfRangeException(nameof(expectedAnnualReturn), "Expected annual return must be greater than -100%.");
        if (currentAge < 0)
            throw new ArgumentOutOfRangeException(nameof(currentAge), "Current age cannot be negative.");
        if (lifeExpectancyAge <= currentAge)
            throw new ArgumentOutOfRangeException(nameof(lifeExpectancyAge), "Life expectancy age must be greater than current age.");

        var path = new List<FireDrawdownPoint>(lifeExpectancyAge - currentAge + 1);
        var warnings = new List<FireProjectionWarning>();
        var balance = startingBalance;
        var hasDepleted = false;

        for (var year = 0; year <= lifeExpectancyAge - currentAge; year++)
        {
            var starting = balance;
            var investmentReturn = starting * expectedAnnualReturn;
            var ending = starting + investmentReturn - annualRetirementExpenses;

            path.Add(new FireDrawdownPoint(
                Year: year,
                Age: currentAge + year,
                StartingBalance: starting,
                InvestmentReturn: investmentReturn,
                AnnualWithdrawal: annualRetirementExpenses,
                NetCashFlow: -annualRetirementExpenses,
                EndingBalance: ending));

            if (!hasDepleted && ending <= 0m)
            {
                hasDepleted = true;
                warnings.Add(new FireProjectionWarning(
                    FireProjectionWarningCode.DrawdownDepletesBeforeLifeExpectancy,
                    "退休後資產會在預期壽命前耗盡。"));
            }

            balance = ending;
        }

        return new FireDrawdownProjection(path, warnings);
    }
}
