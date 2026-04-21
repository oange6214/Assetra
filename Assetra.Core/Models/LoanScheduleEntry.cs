namespace Assetra.Core.Models;

/// <summary>
/// Single row in an amortization schedule (攤還表).
/// Generated once at loan creation by AmortizationService; stored in loan_schedule table.
/// </summary>
public sealed record LoanScheduleEntry(
    Guid     Id,
    Guid     AssetId,          // FK → asset.id (the liability asset)
    int      Period,           // 1-based period number
    DateOnly DueDate,          // scheduled payment date
    decimal  TotalAmount,      // monthly payment (principal + interest)
    decimal  PrincipalAmount,  // portion reducing the loan balance
    decimal  InterestAmount,   // interest expense (does not reduce balance)
    decimal  Remaining,        // outstanding balance AFTER this payment
    bool     IsPaid,
    DateTime? PaidAt,          // actual payment timestamp
    Guid?    TradeId);         // linked LoanRepay trade id
