using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models.Fire;

namespace Assetra.Application.Fire;

/// <summary>
/// FIRE（Financial Independence, Retire Early）計算服務。
/// 採用年度離散模型：每年餘額 = 期初 × (1 + 報酬率) + 年儲蓄。
/// FIRE 目標 = 年支出 ÷ 安全提領率（4% rule → 25 倍年支出）。
/// </summary>
public sealed class FireCalculatorService : IFireCalculatorService
{
    public FireProjection Calculate(FireInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.WithdrawalRate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(inputs.WithdrawalRate), "Withdrawal rate must be positive.");
        if (inputs.WithdrawalRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(inputs.WithdrawalRate), "Withdrawal rate must be less than or equal to 100%.");
        if (inputs.AnnualExpenses <= 0m)
            throw new ArgumentOutOfRangeException(nameof(inputs.AnnualExpenses), "Annual expenses must be positive.");
        if (inputs.CurrentNetWorth < 0m)
            throw new ArgumentOutOfRangeException(nameof(inputs.CurrentNetWorth), "Current net worth cannot be negative.");
        if (inputs.AnnualSavings < 0m)
            throw new ArgumentOutOfRangeException(nameof(inputs.AnnualSavings), "Annual savings cannot be negative.");
        if (inputs.ExpectedAnnualReturn <= -1m)
            throw new ArgumentOutOfRangeException(nameof(inputs.ExpectedAnnualReturn), "Expected annual return must be greater than -100%.");
        if (inputs.MaxYears <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputs.MaxYears), "MaxYears must be positive.");

        var fireNumber = inputs.AnnualExpenses / inputs.WithdrawalRate;
        var path = new List<decimal>(inputs.MaxYears + 1) { inputs.CurrentNetWorth };

        var balance = inputs.CurrentNetWorth;
        int? yearsToFire = balance >= fireNumber ? 0 : null;

        for (int year = 1; year <= inputs.MaxYears; year++)
        {
            balance = balance * (1m + inputs.ExpectedAnnualReturn) + inputs.AnnualSavings;
            path.Add(balance);
            if (yearsToFire is null && balance >= fireNumber)
                yearsToFire = year;
        }

        var projectedAtFire = yearsToFire.HasValue ? path[yearsToFire.Value] : balance;

        return new FireProjection(
            FireNumber: fireNumber,
            YearsToFire: yearsToFire,
            ProjectedNetWorthAtFire: projectedAtFire,
            WealthPath: path);
    }
}
