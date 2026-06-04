namespace Assetra.Core.Models.Calculators;
public sealed record RentVsBuyInputs(
    decimal HomePrice, decimal DownPayment, decimal MortgageAnnualRate, int LoanYears,
    decimal AnnualHoldingCostRate, decimal AnnualAppreciation,
    decimal MonthlyRent, decimal AnnualRentIncrease, int CompareYears);
public sealed record RentVsBuyResult(decimal BuyNetCost, decimal RentNetCost, int? BreakEvenYear, bool BuyCheaper);
