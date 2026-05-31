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
    private readonly CollectionViewSource _expenseViewSource = new();
    private readonly CollectionViewSource _incomeViewSource = new();
    /// <summary>本月「未使用」的支出 / 收入分類，預設摺疊。</summary>
    private readonly CollectionViewSource _inactiveExpenseViewSource = new();
    private readonly CollectionViewSource _inactiveIncomeViewSource = new();

    private readonly ObservableCollection<CategoryRowViewModel> _categories = [];
    private readonly ObservableCollection<AutoCategorizationRuleRowViewModel> _rules = [];
    private readonly ObservableCollection<BudgetRowViewModel> _budgets = [];

    public ReadOnlyObservableCollection<CategoryRowViewModel> Categories { get; }
    public ReadOnlyObservableCollection<AutoCategorizationRuleRowViewModel> Rules { get; }
    public ReadOnlyObservableCollection<BudgetRowViewModel> Budgets { get; }

    public ICollectionView ExpenseView { get; }

    /// <summary>本月「未使用」支出 / 收入分類列表 — disclosure 區域用。</summary>
    public ICollectionView InactiveExpenseView { get; }
    public ICollectionView InactiveIncomeView { get; }

    /// <summary>是否展開「本月未使用」支出區塊。預設 false（折疊）。</summary>
    [ObservableProperty] private bool _showInactiveExpenses;
    /// <summary>是否展開「本月未使用」收入區塊。預設 false（折疊）。</summary>
    [ObservableProperty] private bool _showInactiveIncomes;

    /// <summary>本月未使用的支出 / 收入分類數量，標籤顯示用。</summary>
    [ObservableProperty] private int _inactiveExpenseCount;
    [ObservableProperty] private int _inactiveIncomeCount;

    /// <summary>標籤顯示時是否該 disclosure 區整段隱藏（0 筆 → 不顯示 header）。</summary>
    public bool HasInactiveExpenses => InactiveExpenseCount > 0;
    public bool HasInactiveIncomes => InactiveIncomeCount > 0;

    partial void OnInactiveExpenseCountChanged(int value) => OnPropertyChanged(nameof(HasInactiveExpenses));
    partial void OnInactiveIncomeCountChanged(int value) => OnPropertyChanged(nameof(HasInactiveIncomes));

    [RelayCommand]
    private void ToggleInactiveExpenses() => ShowInactiveExpenses = !ShowInactiveExpenses;

    [RelayCommand]
    private void ToggleInactiveIncomes() => ShowInactiveIncomes = !ShowInactiveIncomes;

    /// <summary>
    /// 把分類在它所屬的 view（同 Kind + 同活躍狀態）中往前 / 往後挪一格。
    /// 用 SortOrder 互換 + 兩筆 UpdateAsync 持久化。
    /// 在 view 邊界（已經是第一筆 / 最後一筆）的情況直接 no-op。
    /// </summary>
    [RelayCommand]
    private async Task MoveCategoryUp(CategoryRowViewModel? row)
    {
        if (row is null)
            return;
        var view = ResolveSiblingView(row);
        var siblings = view?.Cast<CategoryRowViewModel>().ToList();
        if (siblings is null)
            return;
        var idx = siblings.IndexOf(row);
        if (idx <= 0)
            return;
        await SwapSortOrderAsync(row, siblings[idx - 1]);
    }

    [RelayCommand]
    private async Task MoveCategoryDown(CategoryRowViewModel? row)
    {
        if (row is null)
            return;
        var view = ResolveSiblingView(row);
        var siblings = view?.Cast<CategoryRowViewModel>().ToList();
        if (siblings is null)
            return;
        var idx = siblings.IndexOf(row);
        if (idx < 0 || idx >= siblings.Count - 1)
            return;
        await SwapSortOrderAsync(row, siblings[idx + 1]);
    }

    /// <summary>選對 row 所屬的 sibling view（active vs inactive × expense vs income）。</summary>
    private ICollectionView? ResolveSiblingView(CategoryRowViewModel row)
    {
        if (row.IsExpense)
            return row.HasMonthlyActivity ? ExpenseView : InactiveExpenseView;
        return row.HasMonthlyActivity ? IncomeView : InactiveIncomeView;
    }

    private async Task SwapSortOrderAsync(CategoryRowViewModel a, CategoryRowViewModel b)
    {
        var tmp = a.SortOrder;
        a.SortOrder = b.SortOrder;
        b.SortOrder = tmp;
        await _repository.UpdateAsync(a.ToModel()).ConfigureAwait(true);
        await _repository.UpdateAsync(b.ToModel()).ConfigureAwait(true);
        RebuildHierarchy(); // 子分類間互換要重新依 SortOrder 排序
        // 重排 view（依 SortOrder 升序）— CollectionView 不會自動偵測 sort key 改變。
        RefreshCategoryViews();
    }
    public ICollectionView IncomeView { get; }

    /// <summary>
    /// 規則表單可選分類列表（含支出與收入，但排除已封存）。
    /// </summary>
    private readonly ObservableCollection<CategoryRowViewModel> _availableCategories = [];
    public ReadOnlyObservableCollection<CategoryRowViewModel> AvailableCategories { get; }

    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private CategoryKind _addKind = CategoryKind.Expense;
    [ObservableProperty] private string _addIcon = string.Empty;
    [ObservableProperty] private string _addColorHex = string.Empty;

    /// <summary>新增分類時選定的父分類；null = 建立為頂層分類。</summary>
    [ObservableProperty] private Guid? _addParentId;

    /// <summary>
    /// 新增分類對話框的「父分類」選項 — 同 Kind 的頂層分類（避免循環巢狀）。
    /// 第一個項目永遠是 ParentOption(null, "（無 — 頂層分類）")。
    /// 由 OnAddKindChanged / RebuildHierarchy 重新整理。
    /// </summary>
    private readonly ObservableCollection<ParentOption> _addParentOptions = [];
    public ReadOnlyObservableCollection<ParentOption> AddParentOptions { get; }
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

    // Collapsible add-form state for each tab
    [ObservableProperty] private bool _isAddCategoryOpen;
    [ObservableProperty] private bool _isAddRuleOpen;
    [ObservableProperty] private bool _isAddBudgetOpen;
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    private Func<Task>? _pendingDeleteAction;

    // Empty-state predicates per tab
    public bool HasNoExpense => Categories.Count(c => c.Kind == CategoryKind.Expense && (ShowArchived || !c.IsArchived)) == 0;
    public bool HasNoIncome => Categories.Count(c => c.Kind == CategoryKind.Income && (ShowArchived || !c.IsArchived)) == 0;
    public bool HasNoRules => Rules.Count == 0;
    public bool HasRules => Rules.Count > 0;
    public bool HasNoBudgets => Budgets.Count == 0;
    public bool HasBudgets => Budgets.Count > 0;

    [RelayCommand]
    private void OpenAddCategory()
    {
        AddError = string.Empty;
        RefreshAddParentOptions();
        IsAddCategoryOpen = true;
    }
    [RelayCommand] private void CloseAddCategory() { IsAddCategoryOpen = false; AddError = string.Empty; }
    [RelayCommand] private void OpenAddRule() { AddRuleError = string.Empty; IsAddRuleOpen = true; }
    [RelayCommand] private void CloseAddRule() { IsAddRuleOpen = false; AddRuleError = string.Empty; }
    [RelayCommand] private void OpenAddBudget() { AddBudgetError = string.Empty; IsAddBudgetOpen = true; }
    [RelayCommand] private void CloseAddBudget() { IsAddBudgetOpen = false; AddBudgetError = string.Empty; }
    [RelayCommand]
    private void RequestDelete(CategoryRowViewModel row)
    {
        if (row is null)
            return;
        OpenDeleteConfirm(row.Name, () => DeleteAsync(row));
    }

    [RelayCommand]
    private void RequestDeleteRule(AutoCategorizationRuleRowViewModel row)
    {
        if (row is null)
            return;
        OpenDeleteConfirm(row.KeywordPattern, () => DeleteRuleAsync(row));
    }

    [RelayCommand]
    private void RequestDeleteBudget(BudgetRowViewModel row)
    {
        if (row is null)
            return;
        OpenDeleteConfirm(row.CategoryDisplay, () => DeleteBudgetAsync(row));
    }

    public IReadOnlyList<BudgetMode> BudgetModeOptions { get; } =
        [BudgetMode.Monthly, BudgetMode.Yearly];

    public IReadOnlyList<CategoryKind> KindOptions { get; } =
        [CategoryKind.Expense, CategoryKind.Income];

    private readonly ObservableCollection<CategoryVisualOption> _iconOptions = [];
    public ReadOnlyObservableCollection<CategoryVisualOption> IconOptions { get; }

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

        Categories = new ReadOnlyObservableCollection<CategoryRowViewModel>(_categories);
        Rules = new ReadOnlyObservableCollection<AutoCategorizationRuleRowViewModel>(_rules);
        Budgets = new ReadOnlyObservableCollection<BudgetRowViewModel>(_budgets);
        AvailableCategories = new ReadOnlyObservableCollection<CategoryRowViewModel>(_availableCategories);
        IconOptions = new ReadOnlyObservableCollection<CategoryVisualOption>(_iconOptions);
        AddParentOptions = new ReadOnlyObservableCollection<ParentOption>(_addParentOptions);

        // 「活躍」views — 本月有交易的 **頂層** 分類；子分類在 row template
        // 內以巢狀 ItemsControl 渲染。父分類只要自己或任一子分類有活動就算活躍。
        _expenseViewSource.Source = Categories;
        ExpenseView = _expenseViewSource.View;
        ExpenseView.Filter = o => o is CategoryRowViewModel r
            && r.IsExpense
            && r.IsTopLevel
            && (ShowArchived || !r.IsArchived)
            && (r.HasMonthlyActivity || r.Children.Any(child => child.HasMonthlyActivity));

        _incomeViewSource.Source = Categories;
        IncomeView = _incomeViewSource.View;
        IncomeView.Filter = o => o is CategoryRowViewModel r
            && r.IsIncome
            && r.IsTopLevel
            && (ShowArchived || !r.IsArchived)
            && (r.HasMonthlyActivity || r.Children.Any(child => child.HasMonthlyActivity));
        IncomeView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));
        ExpenseView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));

        // 「未使用」views — 頂層且自己 + 全部後代都沒交易。
        _inactiveExpenseViewSource.Source = Categories;
        InactiveExpenseView = _inactiveExpenseViewSource.View;
        InactiveExpenseView.Filter = o => o is CategoryRowViewModel r
            && r.IsExpense
            && r.IsTopLevel
            && (ShowArchived || !r.IsArchived)
            && !r.HasMonthlyActivity
            && !r.Children.Any(child => child.HasMonthlyActivity);
        InactiveExpenseView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));

        _inactiveIncomeViewSource.Source = Categories;
        InactiveIncomeView = _inactiveIncomeViewSource.View;
        InactiveIncomeView.Filter = o => o is CategoryRowViewModel r
            && r.IsIncome
            && r.IsTopLevel
            && (ShowArchived || !r.IsArchived)
            && !r.HasMonthlyActivity
            && !r.Children.Any(child => child.HasMonthlyActivity);
        InactiveIncomeView.SortDescriptions.Add(new SortDescription(nameof(CategoryRowViewModel.SortOrder), ListSortDirection.Ascending));

        _localization.LanguageChanged += OnLanguageChanged;
        RefreshIconOptions();
        ApplyAddDefaults(AddKind);

        _categories.CollectionChanged += (_, _) => NotifyEmptyStatesChanged();
        _rules.CollectionChanged += (_, _) => NotifyEmptyStatesChanged();
        _budgets.CollectionChanged += (_, _) => NotifyEmptyStatesChanged();

        // External trade mutations (new income / expense entered via the
        // shell's "新增紀錄" dialog) bump BudgetChanged → recompute Spent so
        // the progress bars reflect reality without forcing the user to
        // navigate away and back.
        _budgetRefreshNotifier.BudgetChanged += async (_, _) =>
        {
            await RefreshBudgetSpentAsync().ConfigureAwait(true);
        };
    }

    private void NotifyEmptyStatesChanged()
    {
        OnPropertyChanged(nameof(HasNoExpense));
        OnPropertyChanged(nameof(HasNoIncome));
        OnPropertyChanged(nameof(HasNoRules));
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(HasNoBudgets));
        OnPropertyChanged(nameof(HasBudgets));
    }

    private void RefreshCategoryViews()
    {
        ExpenseView?.Refresh();
        IncomeView?.Refresh();
        InactiveExpenseView?.Refresh();
        InactiveIncomeView?.Refresh();
        InactiveExpenseCount = InactiveExpenseView?.Cast<object>().Count() ?? 0;
        InactiveIncomeCount = InactiveIncomeView?.Cast<object>().Count() ?? 0;
        NotifyEmptyStatesChanged();
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
        RefreshCategoryViews();
    }

    partial void OnAddKindChanged(CategoryKind value)
    {
        ApplyAddDefaults(value);
        RefreshAddParentOptions();
    }

    /// <summary>
    /// 重整新增對話框的父分類選項 — 取同 Kind 的頂層分類（IsTopLevel=true 且非封存）。
    /// 永遠在最前加一個「無 — 頂層分類」selector，讓使用者能建立頂層。
    /// </summary>
    private void RefreshAddParentOptions()
    {
        _addParentOptions.Clear();
        _addParentOptions.Add(new ParentOption(null, GetString("Categories.Field.NoParent", "（無 — 頂層分類）")));
        foreach (var c in _categories
            .Where(c => c.Kind == AddKind && c.IsTopLevel && !c.IsArchived)
            .OrderBy(c => c.SortOrder))
        {
            _addParentOptions.Add(new ParentOption(c.Id, c.Name));
        }
        // 若先前選的父分類被刪掉，reset 為 null
        if (AddParentId.HasValue && _addParentOptions.All(o => o.Id != AddParentId))
            AddParentId = null;
    }

    public async Task LoadAsync()
    {
        var data = await _repository.GetAllAsync().ConfigureAwait(true);
        _categories.Clear();
        foreach (var c in data)
            _categories.Add(CategoryRowViewModel.FromModel(c));

        RefreshAvailableCategories();
        await LoadRulesAsync().ConfigureAwait(true);
        await LoadBudgetsAsync().ConfigureAwait(true);
        await RefreshMonthlyUsageAsync().ConfigureAwait(true);
        ApplyBudgetsToCategoryRows();
        RebuildHierarchy();
        // Stats 改變後 filters 不會自動 re-evaluate（CollectionView 只在 collection 變動時觸發），
        // 手動 Refresh 一次讓 active / inactive 重新分組。
        RefreshCategoryViews();
        IsLoaded = true;
    }

    /// <summary>
    /// 用 ParentId 把扁平的 _categories 列表組成父子樹 — 每個父 row 的 Children
    /// 集合填入它的子分類（依 SortOrder 升序）。子分類本身的 Children 永遠空。
    /// 在 LoadAsync 之後 / Insert / Delete / SwapSortOrder 之後都該重建。
    /// </summary>
    private void RebuildHierarchy()
    {
        // 先清空所有 children — 避免重複加入
        foreach (var c in _categories)
            c.Children.Clear();

        var byId = _categories.ToDictionary(c => c.Id);
        var grouped = _categories
            .Where(c => c.ParentId.HasValue)
            .GroupBy(c => c.ParentId!.Value);
        foreach (var g in grouped)
        {
            if (!byId.TryGetValue(g.Key, out var parent))
                continue;
            foreach (var child in g.OrderBy(x => x.SortOrder))
                parent.Children.Add(child);
        }
    }

    /// <summary>
    /// 取出單筆交易應該計入「本月分類使用金額」的數字。
    /// - 收入 / 支出 / 股利等：金額在 <c>CashAmount</c>
    /// - 買 / 賣 / 借款 / 還款等：金額是 <c>Price * Quantity</c>
    /// 一律取絕對值（顯示「流動量」概念，正負語意已由分類 Kind 決定）。
    /// </summary>
    private static decimal TradeAmountForCategory(Trade t)
    {
        if (t.CashAmount.HasValue && t.CashAmount.Value != 0m)
            return Math.Abs(t.CashAmount.Value);
        return Math.Abs(t.Price * t.Quantity);
    }

    /// <summary>
    /// 把本月 / 本年的預算上限投影到對應分類的 row VM 上，讓「收支分類」頁的每一列
    /// 都能就地顯示 progress bar，不必跳到「預算」tab 才看得到。
    /// 優先匹配「本月 Monthly」預算；若無，退而求其次用「本年 Yearly」÷12 當月預算。
    /// 找不到任何匹配的 → BudgetAmount 維持 0，row 不顯示 progress bar。
    /// </summary>
    private void ApplyBudgetsToCategoryRows()
    {
        var today = DateTime.Today;
        var thisMonth = _budgets
            .Where(b => b.CategoryId.HasValue
                     && b.Mode == BudgetMode.Monthly
                     && b.Year == today.Year
                     && b.Month == today.Month)
            .ToDictionary(b => b.CategoryId!.Value, b => b.Amount);
        var thisYear = _budgets
            .Where(b => b.CategoryId.HasValue
                     && b.Mode == BudgetMode.Yearly
                     && b.Year == today.Year)
            .ToDictionary(b => b.CategoryId!.Value, b => b.Amount);

        foreach (var row in _categories)
        {
            if (thisMonth.TryGetValue(row.Id, out var monthBudget))
                row.BudgetAmount = monthBudget;
            else if (thisYear.TryGetValue(row.Id, out var yearBudget))
                row.BudgetAmount = yearBudget / 12m; // 年度預算攤平到月
            else
                row.BudgetAmount = 0m;
        }
    }

    /// <summary>
    /// 計算每個分類「本月」的交易筆數與金額合計。
    /// 用 ABS(amount) 加總，讓支出與收入都用正數顯示（語意：「本月此分類流動 $N」）。
    /// 與 RefreshBudgetSpentAsync 同樣走全部 trades 過濾 + group by 分類，
    /// 對個人理財場景 (trade 數量有限) 性能足夠。
    /// </summary>
    private async Task RefreshMonthlyUsageAsync()
    {
        if (_categories.Count == 0)
            return;

        var today = DateTime.Today;
        var monthStartLocal = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Local);
        var nextMonthLocal = monthStartLocal.AddMonths(1);
        var fromUtc = monthStartLocal.ToUniversalTime();
        var toUtc = nextMonthLocal.ToUniversalTime();

        var allTrades = await _tradeRepository.GetAllAsync().ConfigureAwait(true);
        var thisMonthByCat = allTrades
            .Where(t => t.CategoryId.HasValue
                        && t.TradeDate >= fromUtc
                        && t.TradeDate < toUtc)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), Amount: g.Sum(TradeAmountForCategory)));

        foreach (var row in _categories)
        {
            if (thisMonthByCat.TryGetValue(row.Id, out var stat))
            {
                row.MonthlyCount = stat.Count;
                row.MonthlyAmount = stat.Amount;
            }
            else
            {
                row.MonthlyCount = 0;
                row.MonthlyAmount = 0m;
            }
        }
    }

    private async Task LoadBudgetsAsync()
    {
        var budgets = await _budgetRepository.GetAllAsync().ConfigureAwait(true);
        _budgets.Clear();
        foreach (var b in budgets)
            _budgets.Add(BudgetRowViewModel.FromModel(b, LookupBudgetCategoryDisplay(b.CategoryId)));
        await RefreshBudgetSpentAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Computes <see cref="BudgetRowViewModel.Spent"/> for every budget row by
    /// querying trades within each budget's effective period and summing the
    /// expense legs that match the budget's <see cref="BudgetRowViewModel.CategoryId"/>
    /// (or all expenses when CategoryId is null = 總預算).
    ///
    /// Expense classification mirrors <c>MonthlyBudgetSummaryService</c>:
    /// Withdrawal + CreditCardCharge + LoanRepay (interest portion only). The
    /// duplication is intentional — that service builds a single-month summary
    /// for the dashboard card; here we need per-budget figures across mixed
    /// monthly + yearly periods, so a focused recompute is simpler than
    /// adapting the dashboard service.
    /// </summary>
    private async Task RefreshBudgetSpentAsync()
    {
        if (_budgets.Count == 0)
            return;

        // One full-trade fetch per refresh — budgets typically span a single
        // year; pre-filter by the widest range we care about. ITradeRepository
        // exposes GetByPeriodAsync; here we just fetch all and bucket in-memory
        // because the budget list is small (≤24 rows typical) and the trade
        // count is bounded by the user's history.
        var allTrades = await _tradeRepository.GetAllAsync().ConfigureAwait(true);

        foreach (var row in _budgets)
            row.Spent = ComputeSpentForBudget(row, allTrades);
    }

    private static decimal ComputeSpentForBudget(BudgetRowViewModel row, IEnumerable<Trade> allTrades)
    {
        // Period bounds in local-time, converted to UTC for trade comparison.
        DateTime fromLocal, toExclusiveLocal;
        if (row.Mode == BudgetMode.Yearly)
        {
            fromLocal = new DateTime(row.Year, 1, 1, 0, 0, 0, DateTimeKind.Local);
            toExclusiveLocal = fromLocal.AddYears(1);
        }
        else
        {
            var month = row.Month ?? DateTime.Today.Month;
            fromLocal = new DateTime(row.Year, month, 1, 0, 0, 0, DateTimeKind.Local);
            toExclusiveLocal = fromLocal.AddMonths(1);
        }
        var fromUtc = fromLocal.ToUniversalTime();
        var toUtc = toExclusiveLocal.ToUniversalTime();

        return allTrades
            .Where(t => t.TradeDate >= fromUtc && t.TradeDate < toUtc)
            .Where(t => row.CategoryId is null || t.CategoryId == row.CategoryId)
            .Sum(GetBudgetExpenseAmount);
    }

    private static decimal GetBudgetExpenseAmount(Trade t) => t.Type switch
    {
        TradeType.Withdrawal => t.CashAmount ?? 0m,
        TradeType.CreditCardCharge => t.CashAmount ?? 0m,
        TradeType.LoanRepay => t.InterestPaid ?? 0m,
        _ => 0m,
    };

    private string LookupBudgetCategoryDisplay(Guid? categoryId)
    {
        if (categoryId is null)
            return GetString("Categories.Budget.Total", "（總預算）");
        var c = Categories.FirstOrDefault(x => x.Id == categoryId);
        // Same as LookupCategoryDisplay — Icon is a Fluent symbol name, not emoji.
        // Only return Name; icons should be rendered separately by row template.
        return c is null
            ? GetString("Categories.Rule.UnknownCategory", "（未知分類）")
            : c.Name;
    }

    private async Task LoadRulesAsync()
    {
        var rules = await _ruleRepository.GetAllAsync().ConfigureAwait(true);
        _rules.Clear();
        foreach (var r in rules)
        {
            var row = AutoCategorizationRuleRowViewModel.FromModel(r);
            row.CategoryDisplay = LookupCategoryDisplay(r.CategoryId);
            _rules.Add(row);
        }
    }

    private void RefreshAvailableCategories()
    {
        _availableCategories.Clear();
        foreach (var c in Categories.Where(c => !c.IsArchived).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder))
            _availableCategories.Add(c);
    }

    private string LookupCategoryDisplay(Guid id)
    {
        var c = Categories.FirstOrDefault(x => x.Id == id);
        if (c is null)
            return GetString("Categories.Rule.UnknownCategory", "（未知分類）");
        // Icon 欄存的是 Fluent symbol 名稱（"Home24" / "Briefcase24" ...），不是 emoji。
        // 直接拼進顯示字串會渲染成「Home24 居住」的怪畫面 — 只取 Name；icon 由
        // 上層 row template 用 ds:AppIcon Symbol="{Binding Icon}" 渲染。
        return c.Name;
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
        // 父分類必須跟自己同 Kind 且必須是頂層（不允許三層巢狀）。Defensive check —
        // UI 已過濾選項，但 race condition / 程式錯誤的話用 null 退回頂層比較安全。
        Guid? parentId = null;
        if (AddParentId.HasValue)
        {
            var parent = Categories.FirstOrDefault(c => c.Id == AddParentId.Value);
            if (parent is not null && parent.Kind == AddKind && parent.IsTopLevel)
                parentId = AddParentId;
        }
        var category = new ExpenseCategory(
            Id: Guid.NewGuid(),
            Name: name,
            Kind: AddKind,
            ParentId: parentId,
            Icon: NullIfBlank(AddIcon),
            ColorHex: NullIfBlank(AddColorHex),
            SortOrder: sort,
            IsArchived: false);

        await _repository.AddAsync(category).ConfigureAwait(true);
        _categories.Add(CategoryRowViewModel.FromModel(category));
        RebuildHierarchy();
        RefreshAvailableCategories();
        RefreshAddParentOptions();
        RefreshCategoryViews();

        AddName = string.Empty;
        AddParentId = null;
        ApplyAddDefaults(AddKind);
        IsAddCategoryOpen = false;

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
        if (row is null)
            return;

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
        RefreshBudgetDisplaysFor(row.Id);
        RefreshAvailableCategories();
        _snackbar.Success(string.Format(
            GetString("Categories.Toast.Updated", "已更新分類「{0}」"), row.Name));
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync(CategoryRowViewModel row)
    {
        if (row is null)
            return;
        if (row.IsArchived && HasActiveDuplicateName(row.Id, row.Kind, row.Name))
        {
            _snackbar.Warning(GetString("Categories.Error.NameDuplicate", "已存在同名分類"));
            return;
        }

        row.IsArchived = !row.IsArchived;
        await _repository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        RefreshCategoryViews();
        RefreshAvailableCategories();
        var key = row.IsArchived ? "Categories.Toast.Archived" : "Categories.Toast.Restored";
        var fallback = row.IsArchived ? "已封存「{0}」" : "已還原「{0}」";
        _snackbar.Success(string.Format(GetString(key, fallback), row.Name));
    }

    [RelayCommand]
    private async Task DeleteAsync(CategoryRowViewModel row)
    {
        if (row is null)
            return;
        // M2: previously did N×O(rows) full scans of three repositories then
        // counted in-memory via LINQ — for a long-running user with thousands
        // of trades that's a real cost on every delete attempt. The new
        // CountByCategoryAsync default still fetches all rows (interface
        // default), but SQLite-backed implementations can override with a
        // proper SQL COUNT(*) when this becomes a hotspot. Same call site —
        // no further changes needed when the SQL override lands.
        var budgetRefs = Budgets.Count(b => b.CategoryId == row.Id);
        var ruleRefs = Rules.Count(r => r.CategoryId == row.Id);
        var tradeRefs = await _tradeRepository.CountByCategoryAsync(row.Id).ConfigureAwait(true);
        var recurringRefs = await _recurringRepository.CountByCategoryAsync(row.Id).ConfigureAwait(true);
        var pendingRefs = await _pendingRecurringRepository.CountByCategoryAsync(row.Id).ConfigureAwait(true);
        if (budgetRefs > 0 || ruleRefs > 0 || tradeRefs > 0 || recurringRefs > 0 || pendingRefs > 0)
        {
            var message = GetDeleteBlockedMessage(row.Name, budgetRefs, ruleRefs, tradeRefs, recurringRefs, pendingRefs);
            _snackbar.Warning(message);
            return;
        }
        await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
        _categories.Remove(row);
        RebuildHierarchy();
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
        _rules.Add(row);

        AddRuleKeyword = string.Empty;
        AddRulePriority = 0;
        AddRuleCaseSensitive = false;
        AddRuleName = string.Empty;
        AddRuleMatchField = AutoCategorizationMatchField.AnyText;
        AddRuleMatchType = AutoCategorizationMatchType.Contains;
        AddRuleAppliesToManual = true;
        AddRuleAppliesToImport = true;
        IsAddRuleOpen = false;

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
        if (row is null)
            return;

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
        if (row is null)
            return;
        row.IsEnabled = !row.IsEnabled;
        await _ruleRepository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(AutoCategorizationRuleRowViewModel row)
    {
        if (row is null)
            return;
        await _ruleRepository.RemoveAsync(row.Id).ConfigureAwait(true);
        _rules.Remove(row);
        _snackbar.Success(string.Format(
            GetString("Categories.Rule.Toast.Deleted", "已刪除規則「{0}」"), row.KeywordPattern));
    }

    private AutoCategorizationScope ResolveAddRuleScope()
    {
        var scope = AutoCategorizationScope.None;
        if (AddRuleAppliesToManual)
            scope |= AutoCategorizationScope.Manual;
        if (AddRuleAppliesToImport)
            scope |= AutoCategorizationScope.Import;
        return scope == AutoCategorizationScope.None ? AutoCategorizationScope.Both : scope;
    }

    private void RefreshRuleDisplaysFor(Guid categoryId)
    {
        foreach (var r in Rules.Where(r => r.CategoryId == categoryId))
            r.CategoryDisplay = LookupCategoryDisplay(categoryId);
    }

    private void RefreshBudgetDisplaysFor(Guid categoryId)
    {
        foreach (var b in Budgets.Where(b => b.CategoryId == categoryId))
            b.CategoryDisplay = LookupBudgetCategoryDisplay(categoryId);
    }

    private bool HasActiveDuplicateName(Guid id, CategoryKind kind, string name) =>
        Categories.Any(c => c.Id != id
                         && !c.IsArchived
                         && c.Kind == kind
                         && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

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
        _budgets.Add(BudgetRowViewModel.FromModel(budget, LookupBudgetCategoryDisplay(categoryId)));
        // Refresh Spent for the new row (and any others affected by overlap).
        await RefreshBudgetSpentAsync().ConfigureAwait(true);
        _budgetRefreshNotifier.NotifyChanged();

        AddBudgetAmount = 0m;
        AddBudgetNote = string.Empty;
        IsAddBudgetOpen = false;

        _snackbar.Success(GetString("Categories.Budget.Toast.Added", "已新增預算"));
    }

    [RelayCommand]
    private void BeginEditBudget(BudgetRowViewModel row) => row?.EnterEditMode();

    [RelayCommand]
    private void CancelEditBudget(BudgetRowViewModel row) => row?.CancelEditMode();

    [RelayCommand]
    private async Task SaveEditBudgetAsync(BudgetRowViewModel row)
    {
        if (row is null)
            return;
        if (row.EditAmount <= 0m)
        {
            row.EditError = GetString("Categories.Budget.Error.AmountRequired", "請輸入大於 0 的金額");
            return;
        }
        row.Amount = row.EditAmount;
        row.Note = NullIfBlank(row.EditNote);
        row.IsEditing = false;
        await _budgetRepository.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        // Spent itself didn't change but Remaining/ProgressPercent/IsOverBudget
        // depend on Amount which we just edited — the [NotifyPropertyChangedFor]
        // on Amount handles those. No explicit RefreshBudgetSpentAsync needed.
        _budgetRefreshNotifier.NotifyChanged();
        _snackbar.Success(GetString("Categories.Budget.Toast.Updated", "已更新預算"));
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(BudgetRowViewModel row)
    {
        if (row is null)
            return;
        await _budgetRepository.RemoveAsync(row.Id).ConfigureAwait(true);
        _budgets.Remove(row);
        _budgetRefreshNotifier.NotifyChanged();
        _snackbar.Success(GetString("Categories.Budget.Toast.Deleted", "已刪除預算"));
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        var action = _pendingDeleteAction;
        CancelDelete();
        if (action is not null)
            await action().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteAction = null;
        DeleteTargetName = string.Empty;
        IsDeleteConfirmOpen = false;
    }

    private void OpenDeleteConfirm(string? targetName, Func<Task> action)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return;

        _pendingDeleteAction = action;
        DeleteTargetName = targetName.Trim();
        IsDeleteConfirmOpen = true;
    }

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
        _iconOptions.Clear();
        foreach (var option in BuildIconOptions())
            _iconOptions.Add(option);
    }

    // Value 為 Fluent System Icons symbol name（例：FoodToast24），由 ds:AppIcon 解析渲染，
    // 與 navrail / dialog 風格保持一致。Label 仍走語系 key。
    private IReadOnlyList<CategoryVisualOption> BuildIconOptions() =>
    [
        new("FoodToast24", GetString("Categories.Icon.Food", "飲食")),
        new("VehicleSubway24", GetString("Categories.Icon.Transport", "交通")),
        new("Home24", GetString("Categories.Icon.Home", "居住")),
        new("Lightbulb24", GetString("Categories.Icon.Utilities", "水電")),
        new("Phone24", GetString("Categories.Icon.Communication", "通訊")),
        new("ShoppingBag24", GetString("Categories.Icon.Shopping", "購物")),
        new("Filmstrip24", GetString("Categories.Icon.Entertainment", "娛樂")),
        new("Stethoscope24", GetString("Categories.Icon.Medical", "醫療")),
        new("BookOpen24", GetString("Categories.Icon.Education", "教育")),
        new("ShieldCheckmark24", GetString("Categories.Icon.Insurance", "保險")),
        new("ArrowSync24", GetString("Categories.Icon.Subscription", "訂閱")),
        new("MoneyDismiss24", GetString("Categories.Icon.Expense", "支出")),
        new("Briefcase24", GetString("Categories.Icon.Salary", "薪資")),
        new("Gift24", GetString("Categories.Icon.Bonus", "獎金")),
        new("BuildingBank24", GetString("Categories.Icon.Interest", "利息")),
        new("Receipt24", GetString("Categories.Icon.TaxRefund", "退稅")),
        new("Money24", GetString("Categories.Icon.Income", "收入")),
        new("ArrowTrendingLines24", GetString("Categories.Icon.Investment", "投資")),
        new("Airplane24", GetString("Categories.Icon.Travel", "旅遊")),
        new("Run24", GetString("Categories.Icon.Sports", "運動")),
        new("People24", GetString("Categories.Icon.Family", "家庭")),
        new("AnimalPawPrint24", GetString("Categories.Icon.Pet", "寵物"))
    ];

    private void ApplyAddDefaults(CategoryKind kind)
    {
        AddIcon = kind == CategoryKind.Income ? "Briefcase24" : "FoodToast24";
        AddColorHex = kind == CategoryKind.Income ? "#22C55E" : "#F59E0B";
    }
}

public sealed record CategoryVisualOption(string Value, string Label);

/// <summary>
/// 新增 / 編輯分類對話框的父分類選項。Id=null 代表「無父 — 建立為頂層」。
/// 顯示在 ComboBox 內，ItemTemplate 綁 Display。
/// </summary>
public sealed record ParentOption(Guid? Id, string Display);
