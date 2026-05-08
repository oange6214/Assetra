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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remaining))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(IsOverBudget))]
    [NotifyPropertyChangedFor(nameof(IsNearBudget))]
    [NotifyPropertyChangedFor(nameof(SpentVsBudgetDisplay))]
    private decimal _amount;

    [ObservableProperty] private string? _note;

    /// <summary>
    /// Cumulative expense across this budget's effective period (month / year),
    /// filtered by <see cref="CategoryId"/> when set, otherwise all expense
    /// trade types. Computed by <see cref="CategoriesViewModel"/> after
    /// loading; defaults to 0 until first compute completes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remaining))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(IsOverBudget))]
    [NotifyPropertyChangedFor(nameof(IsNearBudget))]
    [NotifyPropertyChangedFor(nameof(SpentVsBudgetDisplay))]
    private decimal _spent;

    /// <summary>Budget − Spent. Negative when over budget.</summary>
    public decimal Remaining => Amount - Spent;

    /// <summary>0–100 scaled percentage of budget consumed; clamped at 100 for the progress bar.</summary>
    public double ProgressPercent => Amount <= 0
        ? 0
        : Math.Clamp((double)(Spent / Amount * 100m), 0, 100);

    /// <summary>True once spending exceeds the configured budget.</summary>
    public bool IsOverBudget => Amount > 0 && Spent > Amount;

    /// <summary>True when 80% ≤ spent &lt; 100% — used to colour-warn before going over.</summary>
    public bool IsNearBudget => Amount > 0 && Spent >= Amount * 0.8m && Spent < Amount;

    /// <summary>"已支出 X / Y" style display string for the row template.</summary>
    public string SpentVsBudgetDisplay => $"{Spent:N0} / {Amount:N0}";

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
