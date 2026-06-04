namespace Assetra.Core.Models.Calculators;
public sealed record LoanAmortizationInputs(decimal Principal, decimal AnnualRate, int Months);
public sealed record LoanPaymentRow(int Month, decimal BeginBalance, decimal Payment, decimal Principal, decimal Interest, decimal EndBalance);
public sealed record LoanAmortizationSchedule(decimal MonthlyPayment, decimal TotalPayment, decimal TotalInterest, IReadOnlyList<LoanPaymentRow> Rows);
