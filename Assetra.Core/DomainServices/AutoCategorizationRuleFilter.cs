using Assetra.Core.Models;

namespace Assetra.Core.DomainServices;

public static class AutoCategorizationRuleFilter
{
    public static IReadOnlyList<AutoCategorizationRule> ForTradeType(
        IEnumerable<AutoCategorizationRule> rules,
        IEnumerable<ExpenseCategory> categories,
        TradeType tradeType)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(categories);

        var expectedKind = CategoryKindForTradeType(tradeType);
        if (expectedKind is null)
            return [];

        var allowedCategoryIds = categories
            .Where(c => !c.IsArchived && c.Kind == expectedKind.Value)
            .Select(c => c.Id)
            .ToHashSet();

        if (allowedCategoryIds.Count == 0)
            return [];

        return rules
            .Where(r => allowedCategoryIds.Contains(r.CategoryId))
            .ToList();
    }

    public static CategoryKind? CategoryKindForTradeType(TradeType tradeType) =>
        tradeType switch
        {
            TradeType.Income or TradeType.Deposit => CategoryKind.Income,
            TradeType.Withdrawal or TradeType.CreditCardCharge or TradeType.LoanRepay => CategoryKind.Expense,
            _ => null,
        };
}
