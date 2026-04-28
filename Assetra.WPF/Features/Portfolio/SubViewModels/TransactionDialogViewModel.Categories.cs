using System.Collections.ObjectModel;
using Assetra.Core.DomainServices;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Features.Categories;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// TransactionDialogViewModel partial — P1 收支管理：分類下拉與自動分類規則。
/// Loads expense / income category rows and auto-categorization rules; matches the
/// transaction note against the rule cache to suggest a category, while preserving
/// any explicit user selection.
/// </summary>
public partial class TransactionDialogViewModel
{
    /// <summary>支出分類（已過濾封存與排序）。供 CashFlow 等支出表單下拉選用。</summary>
    public ObservableCollection<CategoryRowViewModel> ExpenseCategories { get; } = [];

    /// <summary>收入分類（已過濾封存與排序）。供 Income 表單下拉選用。</summary>
    public ObservableCollection<CategoryRowViewModel> IncomeCategories { get; } = [];

    [ObservableProperty] private Guid? _txCategoryId;
    private bool _txCategoryAutoMatched;
    private bool _suppressCategoryAutoTracking;

    private async Task LoadCategoriesAsync()
    {
        try
        {
            if (_categoryRepository is not null)
            {
                var cats = await _categoryRepository.GetAllAsync().ConfigureAwait(true);
                ExpenseCategories.Clear();
                IncomeCategories.Clear();
                foreach (var c in cats.Where(x => !x.IsArchived).OrderBy(x => x.SortOrder))
                {
                    var row = CategoryRowViewModel.FromModel(c);
                    if (c.Kind == CategoryKind.Expense) ExpenseCategories.Add(row);
                    else IncomeCategories.Add(row);
                }
            }
            if (_ruleRepository is not null)
            {
                _autoRulesCache = await _ruleRepository.GetAllAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load categories/rules for transaction dialog");
        }
    }

    partial void OnTxNoteChanged(string value)
    {
        // 僅當使用者尚未手動指定分類，或上次是被自動匹配的，才重新自動匹配
        if (!_txCategoryAutoMatched && TxCategoryId.HasValue) return;
        if (_autoRulesCache.Count == 0) return;
        var match = AutoCategorizationEngine.Match(value, _autoRulesCache);
        _suppressCategoryAutoTracking = true;
        try
        {
            if (match.HasValue)
            {
                TxCategoryId = match.Value;
                _txCategoryAutoMatched = true;
            }
            else if (_txCategoryAutoMatched)
            {
                TxCategoryId = null;
                _txCategoryAutoMatched = false;
            }
        }
        finally { _suppressCategoryAutoTracking = false; }
    }

    partial void OnTxCategoryIdChanged(Guid? value)
    {
        // 使用者主動透過下拉變更 → 取消自動匹配旗標，避免 Note 變更覆寫使用者選擇
        if (!_suppressCategoryAutoTracking)
            _txCategoryAutoMatched = false;
    }
}
