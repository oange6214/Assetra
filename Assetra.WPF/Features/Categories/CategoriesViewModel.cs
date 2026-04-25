using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Categories;

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoryRepository _repository;
    private readonly IAutoCategorizationRuleRepository _ruleRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ISnackbarService _snackbar;
    private readonly ILocalizationService _localization;

    public ObservableCollection<CategoryRowViewModel> Categories { get; } = [];
    public ObservableCollection<AutoCategorizationRuleRowViewModel> Rules { get; } = [];
    public ObservableCollection<BudgetRowViewModel> Budgets { get; } = [];

    public ICollectionView ExpenseView { get; }
    public ICollectionView IncomeView { get; }

    /// <summary>
    /// 規則表單可選分類列表（含支出與收入，但排除已封存）。
    /// </summary>
    public ObservableCollection<CategoryRowViewModel> AvailableCategories { get; } = [];

    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private CategoryKind _addKind = CategoryKind.Expense;
    [ObservableProperty] private string _addIcon = string.Empty;
    [ObservableProperty] private string _addColorHex = string.Empty;
    [ObservableProperty] private string _addError = string.Empty;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _showArchived;

    // Rule add-form
    [ObservableProperty] private string _addRuleKeyword = string.Empty;
    [ObservableProperty] private CategoryRowViewModel? _addRuleCategory;
    [ObservableProperty] private int _addRulePriority;
    [ObservableProperty] private bool _addRuleCaseSensitive;
    [ObservableProperty] private string _addRuleError = string.Empty;

    // Budget add-form
    [ObservableProperty] private CategoryRowViewModel? _addBudgetCategory;
    [ObservableProperty] private BudgetMode _addBudgetMode = BudgetMode.Monthly;
    [ObservableProperty] private int _addBudgetYear = DateTime.Today.Year;
    [ObservableProperty] private int _addBudgetMonth = DateTime.Today.Month;
    [ObservableProperty] private decimal _addBudgetAmount;
    [ObservableProperty] private string _addBudgetNote = string.Empty;
    [ObservableProperty] private string _addBudgetError = string.Empty;

    public IReadOnlyList<BudgetMode> BudgetModeOptions { get; } =
        [BudgetMode.Monthly, BudgetMode.Yearly];

    public IReadOnlyList<CategoryKind> KindOptions { get; } =
        [CategoryKind.Expense, CategoryKind.Income];

    public CategoriesViewModel(
        ICategoryRepository repository,
        IAutoCategorizationRuleRepository ruleRepository,
        IBudgetRepository budgetRepository,
        ISnackbarService snackbar,
        ILocalizationService localization)
    {
        _repository = repository;
        _ruleRepository = ruleRepository;
        _budgetRepository = budgetRepository;
        _snackbar = snackbar;
        _localization = localization;

        ExpenseView = CollectionViewSource.GetDefaultView(Categories);
        ExpenseView.Filter = o => o is CategoryRowViewModel r && r.IsExpense && (ShowArchived || !r.IsArchived);

        // Income column needs its own view — use a wrapper observable collection bound separately.
        IncomeView = new CollectionViewSource { Source = Categories }.View;
        IncomeView.Filter = o => o is CategoryRowViewModel r && r.IsIncome && (ShowArchived || !r.IsArchived);
        IncomeView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));
        ExpenseView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));

        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var rule in Rules)
            rule.CategoryDisplay = LookupCategoryDisplay(rule.CategoryId);
        foreach (var budget in Budgets)
            budget.CategoryDisplay = LookupBudgetCategoryDisplay(budget.CategoryId);
    }

    partial void OnShowArchivedChanged(bool value)
    {
        ExpenseView.Refresh();
        IncomeView.Refresh();
    }

    public async Task LoadAsync()
    {
        var data = await _repository.GetAllAsync().ConfigureAwait(true);
        Categories.Clear();
        foreach (var c in data)
            Categories.Add(CategoryRowViewModel.FromModel(c));

        RefreshAvailableCategories();
        await LoadRulesAsync().ConfigureAwait(true);
        await LoadBudgetsAsync().ConfigureAwait(true);
        IsLoaded = true;
    }

    private async Task LoadBudgetsAsync()
    {
        var budgets = await _budgetRepository.GetAllAsync().ConfigureAwait(true);
        Budgets.Clear();
        foreach (var b in budgets)
            Budgets.Add(BudgetRowViewModel.FromModel(b, LookupBudgetCategoryDisplay(b.CategoryId)));
    }

    private string LookupBudgetCategoryDisplay(Guid? categoryId)
    {
        if (categoryId is null)
            return GetString("Categories.Budget.Total", "（總預算）");
        var c = Categories.FirstOrDefault(x => x.Id == categoryId);
        return c is null
            ? GetString("Categories.Rule.UnknownCategory", "（未知分類）")
            : string.IsNullOrEmpty(c.Icon) ? c.Name : $"{c.Icon} {c.Name}";
    }

    private async Task LoadRulesAsync()
    {
        var rules = await _ruleRepository.GetAllAsync().ConfigureAwait(true);
        Rules.Clear();
        foreach (var r in rules)
        {
            var row = AutoCategorizationRuleRowViewModel.FromModel(r);
            row.CategoryDisplay = LookupCategoryDisplay(r.CategoryId);
            Rules.Add(row);
        }
    }

    private void RefreshAvailableCategories()
    {
        AvailableCategories.Clear();
        foreach (var c in Categories.Where(c => !c.IsArchived).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder))
            AvailableCategories.Add(c);
    }

    private string LookupCategoryDisplay(Guid id)
    {
        var c = Categories.FirstOrDefault(x => x.Id == id);
        return c is null
            ? GetString("Categories.Rule.UnknownCategory", "（未知分類）")
            : string.IsNullOrEmpty(c.Icon) ? c.Name : $"{c.Icon} {c.Name}";
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        AddError = string.Empty;
        var name = AddName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            AddError = GetString("Categories.Error.NameRequired", "請輸入分類名稱");
            return;
        }
        if (Categories.Any(c => !c.IsArchived
                             && c.Kind == AddKind
                             && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AddError = GetString("Categories.Error.NameDuplicate", "已存在同名分類");
            return;
        }

        var sort = (Categories.Where(c => c.Kind == AddKind).Select(c => c.SortOrder).DefaultIfEmpty(0).Max()) + 1;
        var category = new ExpenseCategory(
            Id: Guid.NewGuid(),
            Name: name,
            Kind: AddKind,
            ParentId: null,
            Icon: NullIfBlank(AddIcon),
            ColorHex: NullIfBlank(AddColorHex),
            SortOrder: sort,
            IsArchived: false);

        await _repository.AddAsync(category).ConfigureAwait(true);
        Categories.Add(CategoryRowViewModel.FromModel(category));
        RefreshAvailableCategories();

        AddName = string.Empty;
        AddIcon = string.Empty;
        AddColorHex = string.Empty;

        _snackbar.Success(string.Format(
            GetString("Categories.Toast.Added", "已新增分類「{0}」"), name));
    }

    [RelayCommand]
    private void BeginEdit(CategoryRowViewModel row) => row?.EnterEditMode();

    [RelayCommand]
    private void CancelEdit(CategoryRowViewModel row) => row?.CancelEditMode();

    [RelayCommand]
    private async Task SaveEditAsync(CategoryRowViewModel row)
    {
        if (row is null) return;

        var name = row.EditName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            row.EditError = GetString("Categories.Error.NameRequired", "請輸入分類名稱");
            return;
        }
        if (Categories.Any(c => c.Id != row.Id
                             && !c.IsArchived
                             && c.Kind == row.Kind
                             && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            row.EditError = GetString("Categories.Error.NameDuplicate", "已存在同名分類");
            return;
        }

        row.Name = name;
        row.Icon = NullIfBlank(row.EditIcon);
        row.ColorHex = NullIfBlank(row.EditColorHex);
        row.IsEditing = false;

        await _repository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        RefreshRuleDisplaysFor(row.Id);
        RefreshAvailableCategories();
        _snackbar.Success(string.Format(
            GetString("Categories.Toast.Updated", "已更新分類「{0}」"), row.Name));
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync(CategoryRowViewModel row)
    {
        if (row is null) return;
        row.IsArchived = !row.IsArchived;
        await _repository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        ExpenseView.Refresh();
        IncomeView.Refresh();
        RefreshAvailableCategories();
        var key = row.IsArchived ? "Categories.Toast.Archived" : "Categories.Toast.Restored";
        var fallback = row.IsArchived ? "已封存「{0}」" : "已還原「{0}」";
        _snackbar.Success(string.Format(GetString(key, fallback), row.Name));
    }

    [RelayCommand]
    private async Task DeleteAsync(CategoryRowViewModel row)
    {
        if (row is null) return;
        await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
        Categories.Remove(row);
        RefreshAvailableCategories();
        _snackbar.Success(string.Format(
            GetString("Categories.Toast.Deleted", "已刪除分類「{0}」"), row.Name));
    }

    // ── Rules ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddRuleAsync()
    {
        AddRuleError = string.Empty;
        var keyword = AddRuleKeyword.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            AddRuleError = GetString("Categories.Rule.Error.KeywordRequired", "請輸入關鍵字");
            return;
        }
        if (AddRuleCategory is null)
        {
            AddRuleError = GetString("Categories.Rule.Error.CategoryRequired", "請選擇分類");
            return;
        }

        var rule = new AutoCategorizationRule(
            Id: Guid.NewGuid(),
            KeywordPattern: keyword,
            CategoryId: AddRuleCategory.Id,
            Priority: AddRulePriority,
            IsEnabled: true,
            MatchCaseSensitive: AddRuleCaseSensitive);

        await _ruleRepository.AddAsync(rule).ConfigureAwait(true);
        var row = AutoCategorizationRuleRowViewModel.FromModel(rule);
        row.CategoryDisplay = LookupCategoryDisplay(rule.CategoryId);
        Rules.Add(row);

        AddRuleKeyword = string.Empty;
        AddRulePriority = 0;
        AddRuleCaseSensitive = false;

        _snackbar.Success(string.Format(
            GetString("Categories.Rule.Toast.Added", "已新增規則「{0}」"), keyword));
    }

    [RelayCommand]
    private void BeginEditRule(AutoCategorizationRuleRowViewModel row) => row?.EnterEditMode();

    [RelayCommand]
    private void CancelEditRule(AutoCategorizationRuleRowViewModel row) => row?.CancelEditMode();

    [RelayCommand]
    private async Task SaveEditRuleAsync(AutoCategorizationRuleRowViewModel row)
    {
        if (row is null) return;

        var keyword = row.EditKeyword.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            row.EditError = GetString("Categories.Rule.Error.KeywordRequired", "請輸入關鍵字");
            return;
        }
        if (row.EditCategoryId == Guid.Empty)
        {
            row.EditError = GetString("Categories.Rule.Error.CategoryRequired", "請選擇分類");
            return;
        }

        row.KeywordPattern = keyword;
        row.CategoryId = row.EditCategoryId;
        row.Priority = row.EditPriority;
        row.MatchCaseSensitive = row.EditCaseSensitive;
        row.CategoryDisplay = LookupCategoryDisplay(row.CategoryId);
        row.IsEditing = false;

        await _ruleRepository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        _snackbar.Success(string.Format(
            GetString("Categories.Rule.Toast.Updated", "已更新規則「{0}」"), row.KeywordPattern));
    }

    [RelayCommand]
    private async Task ToggleRuleEnabledAsync(AutoCategorizationRuleRowViewModel row)
    {
        if (row is null) return;
        row.IsEnabled = !row.IsEnabled;
        await _ruleRepository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(AutoCategorizationRuleRowViewModel row)
    {
        if (row is null) return;
        await _ruleRepository.RemoveAsync(row.Id).ConfigureAwait(true);
        Rules.Remove(row);
        _snackbar.Success(string.Format(
            GetString("Categories.Rule.Toast.Deleted", "已刪除規則「{0}」"), row.KeywordPattern));
    }

    private void RefreshRuleDisplaysFor(Guid categoryId)
    {
        foreach (var r in Rules.Where(r => r.CategoryId == categoryId))
            r.CategoryDisplay = LookupCategoryDisplay(categoryId);
    }

    // ── Budgets ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddBudgetAsync()
    {
        AddBudgetError = string.Empty;
        if (AddBudgetAmount <= 0m)
        {
            AddBudgetError = GetString("Categories.Budget.Error.AmountRequired", "請輸入大於 0 的金額");
            return;
        }
        if (AddBudgetYear < 2000 || AddBudgetYear > 2100)
        {
            AddBudgetError = GetString("Categories.Budget.Error.YearInvalid", "年份不在合理範圍");
            return;
        }
        int? month = AddBudgetMode == BudgetMode.Monthly ? AddBudgetMonth : null;
        if (month.HasValue && (month < 1 || month > 12))
        {
            AddBudgetError = GetString("Categories.Budget.Error.MonthInvalid", "月份必須介於 1~12");
            return;
        }

        var categoryId = AddBudgetCategory?.Id;
        if (Budgets.Any(b => b.CategoryId == categoryId
                          && b.Mode == AddBudgetMode
                          && b.Year == AddBudgetYear
                          && b.Month == month))
        {
            AddBudgetError = GetString("Categories.Budget.Error.Duplicate", "該期間已設定預算");
            return;
        }

        var budget = new Budget(
            Id: Guid.NewGuid(),
            CategoryId: categoryId,
            Mode: AddBudgetMode,
            Year: AddBudgetYear,
            Month: month,
            Amount: AddBudgetAmount,
            Currency: "TWD",
            Note: NullIfBlank(AddBudgetNote));

        await _budgetRepository.AddAsync(budget).ConfigureAwait(true);
        Budgets.Add(BudgetRowViewModel.FromModel(budget, LookupBudgetCategoryDisplay(categoryId)));

        AddBudgetAmount = 0m;
        AddBudgetNote = string.Empty;

        _snackbar.Success(GetString("Categories.Budget.Toast.Added", "已新增預算"));
    }

    [RelayCommand]
    private void BeginEditBudget(BudgetRowViewModel row) => row?.EnterEditMode();

    [RelayCommand]
    private void CancelEditBudget(BudgetRowViewModel row) => row?.CancelEditMode();

    [RelayCommand]
    private async Task SaveEditBudgetAsync(BudgetRowViewModel row)
    {
        if (row is null) return;
        if (row.EditAmount <= 0m)
        {
            row.EditError = GetString("Categories.Budget.Error.AmountRequired", "請輸入大於 0 的金額");
            return;
        }
        row.Amount = row.EditAmount;
        row.Note = NullIfBlank(row.EditNote);
        row.IsEditing = false;
        await _budgetRepository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        _snackbar.Success(GetString("Categories.Budget.Toast.Updated", "已更新預算"));
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(BudgetRowViewModel row)
    {
        if (row is null) return;
        await _budgetRepository.RemoveAsync(row.Id).ConfigureAwait(true);
        Budgets.Remove(row);
        _snackbar.Success(GetString("Categories.Budget.Toast.Deleted", "已刪除預算"));
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);
}
