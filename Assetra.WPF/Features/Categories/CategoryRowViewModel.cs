using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Categories;

public partial class CategoryRowViewModel : ObservableObject
{
    public Guid Id { get; init; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private CategoryKind _kind;
    [ObservableProperty] private string? _icon;
    [ObservableProperty] private string? _colorHex;
    [ObservableProperty] private int _sortOrder;
    [ObservableProperty] private bool _isArchived;

    // Edit-mode buffers
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string? _editIcon;
    [ObservableProperty] private string? _editColorHex;
    [ObservableProperty] private string _editError = string.Empty;

    public bool IsIncome => Kind == CategoryKind.Income;
    public bool IsExpense => Kind == CategoryKind.Expense;

    partial void OnKindChanged(CategoryKind value)
    {
        OnPropertyChanged(nameof(IsIncome));
        OnPropertyChanged(nameof(IsExpense));
    }

    public ExpenseCategory ToModel() => new(
        Id: Id,
        Name: Name,
        Kind: Kind,
        ParentId: null,
        Icon: Icon,
        ColorHex: ColorHex,
        SortOrder: SortOrder,
        IsArchived: IsArchived);

    public static CategoryRowViewModel FromModel(ExpenseCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Kind = c.Kind,
        Icon = c.Icon,
        ColorHex = c.ColorHex,
        SortOrder = c.SortOrder,
        IsArchived = c.IsArchived,
        EditName = c.Name,
        EditIcon = c.Icon,
        EditColorHex = c.ColorHex,
    };

    public void EnterEditMode()
    {
        EditName = Name;
        EditIcon = Icon;
        EditColorHex = ColorHex;
        EditError = string.Empty;
        IsEditing = true;
    }

    public void CancelEditMode()
    {
        EditError = string.Empty;
        IsEditing = false;
    }

    public override string ToString() => Name;
}
