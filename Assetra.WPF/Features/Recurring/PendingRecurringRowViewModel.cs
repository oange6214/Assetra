using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Recurring;

public partial class PendingRecurringRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private Guid _recurringSourceId;
    [ObservableProperty] private DateTime _dueDate;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private TradeType _tradeType;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private PendingStatus _status;
    [ObservableProperty] private string _sourceDisplay = string.Empty;

    public string DueDateDisplay => DueDate.ToString("yyyy-MM-dd");
    public string AmountDisplay => $"NT${Amount:N0}";

    public static PendingRecurringRowViewModel FromModel(PendingRecurringEntry e, string sourceDisplay) => new()
    {
        Id = e.Id,
        RecurringSourceId = e.RecurringSourceId,
        DueDate = e.DueDate,
        Amount = e.Amount,
        TradeType = e.TradeType,
        Note = e.Note,
        Status = e.Status,
        SourceDisplay = sourceDisplay,
    };

    partial void OnDueDateChanged(DateTime value) => OnPropertyChanged(nameof(DueDateDisplay));
    partial void OnAmountChanged(decimal value) => OnPropertyChanged(nameof(AmountDisplay));
}
