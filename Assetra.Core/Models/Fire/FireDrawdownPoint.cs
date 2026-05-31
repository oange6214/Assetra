namespace Assetra.Core.Models.Fire;

public sealed record FireDrawdownPoint(
    int Year,
    int? Age,
    decimal StartingBalance,
    decimal InvestmentReturn,
    decimal AnnualWithdrawal,
    decimal NetCashFlow,
    decimal EndingBalance);
