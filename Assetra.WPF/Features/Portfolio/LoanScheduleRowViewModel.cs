using CommunityToolkit.Mvvm.ComponentModel;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.Portfolio;

public sealed partial class LoanScheduleRowViewModel : ObservableObject
{
    public Guid   Id              { get; }
    public int    Period          { get; }
    public DateOnly DueDate       { get; }
    public decimal TotalAmount    { get; }
    public decimal PrincipalAmount { get; }
    public decimal InterestAmount  { get; }
    public decimal Remaining       { get; }

    [ObservableProperty] private bool     _isPaid;
    [ObservableProperty] private DateTime? _paidAt;
    [ObservableProperty] private Guid?    _tradeId;

    public string DueDateDisplay => DueDate.ToString("yyyy/MM/dd");

    public LoanScheduleRowViewModel(LoanScheduleEntry e)
    {
        Id              = e.Id;
        Period          = e.Period;
        DueDate         = e.DueDate;
        TotalAmount     = e.TotalAmount;
        PrincipalAmount = e.PrincipalAmount;
        InterestAmount  = e.InterestAmount;
        Remaining       = e.Remaining;
        _isPaid         = e.IsPaid;
        _paidAt         = e.PaidAt;
        _tradeId        = e.TradeId;
    }
}
