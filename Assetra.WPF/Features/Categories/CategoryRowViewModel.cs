using System.Collections.ObjectModel;
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

    /// <summary>父分類 Id；null = 頂層分類。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopLevel))]
    [NotifyPropertyChangedFor(nameof(IsSubcategory))]
    private Guid? _parentId;

    /// <summary>True 當這是頂層分類（無父）。</summary>
    public bool IsTopLevel => !ParentId.HasValue;
    /// <summary>True 當這是子分類（有父）— XAML 用來決定縮排。</summary>
    public bool IsSubcategory => ParentId.HasValue;

    /// <summary>
    /// 子分類列表（僅頂層 row 有意義；子分類自身的 Children 永遠是空）。
    /// 由 CategoriesViewModel.RebuildHierarchy 重建。
    /// </summary>
    public ObservableCollection<CategoryRowViewModel> Children { get; }

    public bool HasChildren => Children.Count > 0;

    private void OnChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasChildren));

    /// <summary>本月該分類的交易筆數。0 表示無使用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonthlyActivity))]
    [NotifyPropertyChangedFor(nameof(MonthlyUsageDisplay))]
    private int _monthlyCount;

    /// <summary>本月該分類的交易金額合計（正數，無論支出/收入）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthlyUsageDisplay))]
    [NotifyPropertyChangedFor(nameof(BudgetProgressPercent))]
    [NotifyPropertyChangedFor(nameof(BudgetVsSpentDisplay))]
    [NotifyPropertyChangedFor(nameof(IsBudgetOver))]
    [NotifyPropertyChangedFor(nameof(IsBudgetNear))]
    private decimal _monthlyAmount;

    public bool HasMonthlyActivity => MonthlyCount > 0;

    /// <summary>「本月 N 筆 · $X,XXX」一行字串；無交易顯示「本月未使用」。</summary>
    public string MonthlyUsageDisplay =>
        MonthlyCount > 0
            ? $"本月 {MonthlyCount} 筆 · ${MonthlyAmount:N0}"
            : "本月未使用";

    // ── 本月預算（僅 Expense 分類；無預算時 BudgetAmount = 0）──────────

    /// <summary>本月該分類的預算上限；0 表示未設預算。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBudget))]
    [NotifyPropertyChangedFor(nameof(BudgetProgressPercent))]
    [NotifyPropertyChangedFor(nameof(BudgetVsSpentDisplay))]
    [NotifyPropertyChangedFor(nameof(IsBudgetOver))]
    [NotifyPropertyChangedFor(nameof(IsBudgetNear))]
    private decimal _budgetAmount;

    public bool HasBudget => BudgetAmount > 0;

    /// <summary>0–100 clamped 百分比，progress bar 寬度用。</summary>
    public double BudgetProgressPercent =>
        BudgetAmount <= 0 ? 0 :
        Math.Clamp((double)(MonthlyAmount / BudgetAmount * 100m), 0, 100);

    /// <summary>True 當實際支出超過預算（紅色警告）。</summary>
    public bool IsBudgetOver => BudgetAmount > 0 && MonthlyAmount > BudgetAmount;

    /// <summary>True 當實際支出已達 80% 但未超（橘色提醒）。</summary>
    public bool IsBudgetNear =>
        BudgetAmount > 0 && MonthlyAmount >= BudgetAmount * 0.8m && MonthlyAmount <= BudgetAmount;

    /// <summary>「$12,450 / $15,000 (83%)」格式，row template subtitle 用。</summary>
    public string BudgetVsSpentDisplay
    {
        get
        {
            if (BudgetAmount <= 0)
                return string.Empty;
            var pct = BudgetAmount <= 0 ? 0 : (double)(MonthlyAmount / BudgetAmount * 100m);
            return $"${MonthlyAmount:N0} / ${BudgetAmount:N0} ({pct:F0}%)";
        }
    }

    // Edit-mode buffers
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string? _editIcon;
    [ObservableProperty] private string? _editColorHex;
    [ObservableProperty] private string _editError = string.Empty;

    private readonly ObservableCollection<CategoryVisualOption> _editIconOptions = [];
    public ReadOnlyObservableCollection<CategoryVisualOption> EditIconOptions { get; }

    public CategoryRowViewModel()
    {
        EditIconOptions = new ReadOnlyObservableCollection<CategoryVisualOption>(_editIconOptions);
        Children = [];
        Children.CollectionChanged += OnChildrenChanged;
    }

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
        ParentId: ParentId,
        Icon: Icon,
        ColorHex: ColorHex,
        SortOrder: SortOrder,
        IsArchived: IsArchived);

    public static CategoryRowViewModel FromModel(ExpenseCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Kind = c.Kind,
        ParentId = c.ParentId,
        Icon = c.Icon,
        ColorHex = c.ColorHex,
        SortOrder = c.SortOrder,
        IsArchived = c.IsArchived,
        EditName = c.Name,
        EditIcon = c.Icon,
        EditColorHex = c.ColorHex,
    };

    public void EnterEditMode(IReadOnlyList<CategoryVisualOption>? iconOptions = null, string currentIconLabel = "Current")
    {
        EditName = Name;
        EditIcon = Icon;
        EditColorHex = ColorHex;
        RefreshEditIconOptions(iconOptions, currentIconLabel);
        EditError = string.Empty;
        IsEditing = true;
    }

    public void RefreshEditModeOptions(IReadOnlyList<CategoryVisualOption>? iconOptions = null, string currentIconLabel = "Current")
    {
        RefreshEditIconOptions(iconOptions, currentIconLabel);
    }

    public void CancelEditMode()
    {
        EditError = string.Empty;
        IsEditing = false;
    }

    private void RefreshEditIconOptions(IReadOnlyList<CategoryVisualOption>? iconOptions, string currentIconLabel)
    {
        _editIconOptions.Clear();
        if (iconOptions is not null)
        {
            foreach (var option in iconOptions)
                _editIconOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(EditIcon) && _editIconOptions.All(x => x.Value != EditIcon))
            _editIconOptions.Insert(0, new CategoryVisualOption(EditIcon!, currentIconLabel));
    }

    public override string ToString() => Name;
}
