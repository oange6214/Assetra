using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Categories;

public partial class BudgetRowViewModel : ObservableObject
{
    public Guid Id { get; init; }

    [ObservableProperty] private Guid? _categoryId;
    [ObservableProperty] private string _categoryDisplay = string.Empty;
    [ObservableProperty] private BudgetMode _mode;
    [ObservableProperty] private int _year;
    [ObservableProperty] private int? _month;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string? _note;

    // Edit buffers
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private decimal _editAmount;
    [ObservableProperty] private string? _editNote;
    [ObservableProperty] private string _editError = string.Empty;

    public string PeriodDisplay =>
        Mode == BudgetMode.Yearly ? $"{Year}" : $"{Year}-{Month:D2}";

    partial void OnModeChanged(BudgetMode value) => OnPropertyChanged(nameof(PeriodDisplay));
    partial void OnYearChanged(int value) => OnPropertyChanged(nameof(PeriodDisplay));
    partial void OnMonthChanged(int? value) => OnPropertyChanged(nameof(PeriodDisplay));

    public Budget ToModel() => new(
        Id: Id,
        CategoryId: CategoryId,
        Mode: Mode,
        Year: Year,
        Month: Month,
        Amount: Amount,
        Currency: "TWD",
        Note: Note);

    public static BudgetRowViewModel FromModel(Budget b, string categoryDisplay) => new()
    {
        Id = b.Id,
        CategoryId = b.CategoryId,
        CategoryDisplay = categoryDisplay,
        Mode = b.Mode,
        Year = b.Year,
        Month = b.Month,
        Amount = b.Amount,
        Note = b.Note,
        EditAmount = b.Amount,
        EditNote = b.Note,
    };

    public void EnterEditMode()
    {
        EditAmount = Amount;
        EditNote = Note;
        EditError = string.Empty;
        IsEditing = true;
    }

    public void CancelEditMode()
    {
        EditError = string.Empty;
        IsEditing = false;
    }
}
