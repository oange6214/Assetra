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
    private readonly ITradeRepository _tradeRepository;
    private readonly IRecurringTransactionRepository _recurringRepository;
    private readonly IPendingRecurringEntryRepository _pendingRecurringRepository;
    private readonly IBudgetRefreshNotifier _budgetRefreshNotifier;
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
    [ObservableProperty] private string _addRuleName = string.Empty;
    [ObservableProperty] private AutoCategorizationMatchField _addRuleMatchField = AutoCategorizationMatchField.AnyText;
    [ObservableProperty] private AutoCategorizationMatchType _addRuleMatchType = AutoCategorizationMatchType.Contains;
    [ObservableProperty] private bool _addRuleAppliesToManual = true;
    [ObservableProperty] private bool _addRuleAppliesToImport = true;
    [ObservableProperty] private string _addRuleError = string.Empty;

    public IReadOnlyList<AutoCategorizationMatchField> MatchFieldOptions { get; } =
        [AutoCategorizationMatchField.Counterparty,
         AutoCategorizationMatchField.Memo,
         AutoCategorizationMatchField.Either,
         AutoCategorizationMatchField.AnyText];

    public IReadOnlyList<AutoCategorizationMatchType> MatchTypeOptions { get; } =
        [AutoCategorizationMatchType.Contains,
         AutoCategorizationMatchType.Equals,
         AutoCategorizationMatchType.StartsWith,
         AutoCategorizationMatchType.Regex];

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

    public ObservableCollection<CategoryVisualOption> IconOptions { get; } = [];

    public IReadOnlyList<string> ColorOptions { get; } =
    [
        "#F59E0B",
        "#3B82F6",
        "#8B5CF6",
        "#06B6D4",
        "#0EA5E9",
        "#EC4899",
        "#A855F7",
        "#EF4444",
        "#10B981",
        "#64748B",
        "#F97316",
        "#9CA3AF",
        "#22C55E",
        "#EAB308",
        "#14B8A6",
        "#84CC16"
    ];

    public CategoriesViewModel(
        ICategoryRepository repository,
        IAutoCategorizationRuleRepository ruleRepository,
        IBudgetRepository budgetRepository,
        ITradeRepository tradeRepository,
        IRecurringTransactionRepository recurringRepository,
        IPendingRecurringEntryRepository pendingRecurringRepository,
        IBudgetRefreshNotifier budgetRefreshNotifier,
        ISnackbarService snackbar,
        ILocalizationService localization)
    {
        _repository = repository;
        _ruleRepository = ruleRepository;
        _budgetRepository = budgetRepository;
        _tradeRepository = tradeRepository;
        _recurringRepository = recurringRepository;
        _pendingRecurringRepository = pendingRecurringRepository;
        _budgetRefreshNotifier = budgetRefreshNotifier;
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
        RefreshIconOptions();
        ApplyAddDefaults(AddKind);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshIconOptions();
        var currentIconLabel = GetString("Categories.Field.CurrentIcon", "目前圖示");
        foreach (var category in Categories.Where(x => x.IsEditing))
            category.RefreshEditModeOptions(IconOptions, currentIconLabel);
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

    partial void OnAddKindChanged(CategoryKind value) => ApplyAddDefaults(value);

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
        ApplyAddDefaults(AddKind);

        _snackbar.Success(string.Format(
            GetString("Categories.Toast.Added", "已新增分類「{0}」"), name));
    }

    [RelayCommand]
    private void BeginEdit(CategoryRowViewModel row) =>
        row?.EnterEditMode(
            IconOptions,
            GetString("Categories.Field.CurrentIcon", "Current icon"));

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
        var budgetRefs = Budgets.Count(b => b.CategoryId == row.Id);
        var ruleRefs = Rules.Count(r => r.CategoryId == row.Id);
        var tradeRefs = (await _tradeRepository.GetAllAsync().ConfigureAwait(true)).Count(t => t.CategoryId == row.Id);
        var recurringRefs = (await _recurringRepository.GetAllAsync().ConfigureAwait(true)).Count(r => r.CategoryId == row.Id);
        var pendingRefs = (await _pendingRecurringRepository.GetAllAsync().ConfigureAwait(true)).Count(e => e.CategoryId == row.Id);
        if (budgetRefs > 0 || ruleRefs > 0 || tradeRefs > 0 || recurringRefs > 0 || pendingRefs > 0)
        {
            var message = GetDeleteBlockedMessage(row.Name, budgetRefs, ruleRefs, tradeRefs, recurringRefs, pendingRefs);
            _snackbar.Warning(message);
            return;
        }
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

        var scope = ResolveAddRuleScope();
        var rule = new AutoCategorizationRule(
            Id: Guid.NewGuid(),
            KeywordPattern: keyword,
            CategoryId: AddRuleCategory.Id,
            Priority: AddRulePriority,
            IsEnabled: true,
            MatchCaseSensitive: AddRuleCaseSensitive,
            Name: string.IsNullOrWhiteSpace(AddRuleName) ? null : AddRuleName.Trim(),
            MatchField: AddRuleMatchField,
            MatchType: AddRuleMatchType,
            AppliesTo: scope);

        await _ruleRepository.AddAsync(rule).ConfigureAwait(true);
        var row = AutoCategorizationRuleRowViewModel.FromModel(rule);
        row.CategoryDisplay = LookupCategoryDisplay(rule.CategoryId);
        Rules.Add(row);

        AddRuleKeyword = string.Empty;
        AddRulePriority = 0;
        AddRuleCaseSensitive = false;
        AddRuleName = string.Empty;
        AddRuleMatchField = AutoCategorizationMatchField.AnyText;
        AddRuleMatchType = AutoCategorizationMatchType.Contains;
        AddRuleAppliesToManual = true;
        AddRuleAppliesToImport = true;

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
        row.Name = string.IsNullOrWhiteSpace(row.EditName) ? null : row.EditName.Trim();
        row.MatchField = row.EditMatchField;
        row.MatchType = row.EditMatchType;
        row.AppliesTo = row.ResolveEditAppliesTo();
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

    private AutoCategorizationScope ResolveAddRuleScope()
    {
        var scope = AutoCategorizationScope.None;
        if (AddRuleAppliesToManual) scope |= AutoCategorizationScope.Manual;
        if (AddRuleAppliesToImport) scope |= AutoCategorizationScope.Import;
        return scope == AutoCategorizationScope.None ? AutoCategorizationScope.Both : scope;
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
        _budgetRefreshNotifier.NotifyChanged();

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
        _budgetRefreshNotifier.NotifyChanged();
        _snackbar.Success(GetString("Categories.Budget.Toast.Updated", "已更新預算"));
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(BudgetRowViewModel row)
    {
        if (row is null) return;
        await _budgetRepository.RemoveAsync(row.Id).ConfigureAwait(true);
        Budgets.Remove(row);
        _budgetRefreshNotifier.NotifyChanged();
        _snackbar.Success(GetString("Categories.Budget.Toast.Deleted", "已刪除預算"));
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string GetDeleteBlockedMessage(string categoryName, int budgetRefs, int ruleRefs, int tradeRefs, int recurringRefs, int pendingRefs)
    {
        var parts = new List<string>();
        if (budgetRefs > 0)
            parts.Add(string.Format(GetString("Categories.Error.Reference.Budget", "{0} 筆預算"), budgetRefs));
        if (ruleRefs > 0)
            parts.Add(string.Format(GetString("Categories.Error.Reference.Rule", "{0} 筆自動分類規則"), ruleRefs));
        if (tradeRefs > 0)
            parts.Add(string.Format(GetString("Categories.Error.Reference.Trade", "{0} 筆交易"), tradeRefs));
        if (recurringRefs > 0)
            parts.Add(string.Format(GetString("Categories.Error.Reference.Recurring", "{0} 筆訂閱排程"), recurringRefs));
        if (pendingRefs > 0)
            parts.Add(string.Format(GetString("Categories.Error.Reference.Pending", "{0} 筆待確認排程"), pendingRefs));

        return string.Format(
            GetString(
                "Categories.Error.DeleteBlockedByReferences",
                "分類「{0}」仍被以下資料使用：{1}，請先解除關聯後再刪除"),
            categoryName,
            string.Join(GetString("Categories.Error.Reference.Separator", "、"), parts));
    }

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);

    private void RefreshIconOptions()
    {
        IconOptions.Clear();
        foreach (var option in BuildIconOptions())
            IconOptions.Add(option);
    }

    private IReadOnlyList<CategoryVisualOption> BuildIconOptions() =>
    [
        new("🍱", GetString("Categories.Icon.Food", "飲食")),
        new("🚇", GetString("Categories.Icon.Transport", "交通")),
        new("🏠", GetString("Categories.Icon.Home", "居住")),
        new("💡", GetString("Categories.Icon.Utilities", "水電")),
        new("📱", GetString("Categories.Icon.Communication", "通訊")),
        new("🛍️", GetString("Categories.Icon.Shopping", "購物")),
        new("🎬", GetString("Categories.Icon.Entertainment", "娛樂")),
        new("🏥", GetString("Categories.Icon.Medical", "醫療")),
        new("📚", GetString("Categories.Icon.Education", "教育")),
        new("🛡️", GetString("Categories.Icon.Insurance", "保險")),
        new("🔁", GetString("Categories.Icon.Subscription", "訂閱")),
        new("💸", GetString("Categories.Icon.Expense", "支出")),
        new("💼", GetString("Categories.Icon.Salary", "薪資")),
        new("🎁", GetString("Categories.Icon.Bonus", "獎金")),
        new("🏦", GetString("Categories.Icon.Interest", "利息")),
        new("🧾", GetString("Categories.Icon.TaxRefund", "退稅")),
        new("💰", GetString("Categories.Icon.Income", "收入")),
        new("📈", GetString("Categories.Icon.Investment", "投資")),
        new("✈️", GetString("Categories.Icon.Travel", "旅遊")),
        new("🏃", GetString("Categories.Icon.Sports", "運動")),
        new("👨‍👩‍👧", GetString("Categories.Icon.Family", "家庭")),
        new("🐾", GetString("Categories.Icon.Pet", "寵物"))
    ];

    private void ApplyAddDefaults(CategoryKind kind)
    {
        AddIcon = kind == CategoryKind.Income ? "💼" : "🍱";
        AddColorHex = kind == CategoryKind.Income ? "#22C55E" : "#F59E0B";
    }
}

public sealed record CategoryVisualOption(string Value, string Label);
