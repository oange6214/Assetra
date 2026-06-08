namespace Assetra.Core.Models.Calculators;
public sealed record RentVsBuyInputs(
    decimal HomePrice, decimal DownPayment, decimal MortgageAnnualRate, int LoanYears,
    decimal AnnualHoldingCostRate, decimal AnnualAppreciation,
    decimal MonthlyRent, decimal AnnualRentIncrease, int CompareYears,
    decimal AnnualInvestmentReturn, decimal PurchaseCostRate, decimal SellCostRate);

public sealed record RentVsBuyResult(
    decimal BuyerEndingNetWorth,
    decimal RenterEndingNetWorth,
    decimal NetWorthDifference,
    int? BreakEvenYear,
    bool BuyCheaper,
    decimal HomeValue,
    decimal RemainingLoanBalance,
    decimal RenterInvestmentBalance,
    decimal TotalRentPaid,
    decimal TotalBuyerCashOut,
    decimal PurchaseCost,
    decimal SellCost);
