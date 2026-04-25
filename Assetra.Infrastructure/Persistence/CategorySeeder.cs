using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// 預設收支分類種子資料：首次啟動建立常見收入／支出分類。
/// </summary>
public static class CategorySeeder
{
    public static async Task EnsureSeededAsync(ICategoryRepository repo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        if (await repo.AnyAsync(ct).ConfigureAwait(false))
            return;

        var defaults = BuildDefaults();
        foreach (var c in defaults)
            await repo.AddAsync(c, ct).ConfigureAwait(false);
    }

    private static IEnumerable<ExpenseCategory> BuildDefaults()
    {
        var sort = 0;

        // ── 支出 ─────────────────────────────────────────────────────
        yield return New("飲食", CategoryKind.Expense, ++sort, "🍱", "#F59E0B");
        yield return New("交通", CategoryKind.Expense, ++sort, "🚇", "#3B82F6");
        yield return New("居住", CategoryKind.Expense, ++sort, "🏠", "#8B5CF6");
        yield return New("水電瓦斯", CategoryKind.Expense, ++sort, "💡", "#06B6D4");
        yield return New("通訊", CategoryKind.Expense, ++sort, "📱", "#0EA5E9");
        yield return New("購物", CategoryKind.Expense, ++sort, "🛍️", "#EC4899");
        yield return New("娛樂", CategoryKind.Expense, ++sort, "🎬", "#A855F7");
        yield return New("醫療", CategoryKind.Expense, ++sort, "🏥", "#EF4444");
        yield return New("教育", CategoryKind.Expense, ++sort, "📚", "#10B981");
        yield return New("保險", CategoryKind.Expense, ++sort, "🛡️", "#64748B");
        yield return New("訂閱服務", CategoryKind.Expense, ++sort, "🔁", "#F97316");
        yield return New("其他支出", CategoryKind.Expense, ++sort, "💸", "#9CA3AF");

        // ── 收入 ─────────────────────────────────────────────────────
        yield return New("薪資", CategoryKind.Income, ++sort, "💼", "#22C55E");
        yield return New("獎金", CategoryKind.Income, ++sort, "🎁", "#EAB308");
        yield return New("利息", CategoryKind.Income, ++sort, "🏦", "#14B8A6");
        yield return New("退稅", CategoryKind.Income, ++sort, "🧾", "#84CC16");
        yield return New("其他收入", CategoryKind.Income, ++sort, "💰", "#6B7280");
    }

    private static ExpenseCategory New(string name, CategoryKind kind, int sort, string icon, string color) =>
        new(Id: Guid.NewGuid(), Name: name, Kind: kind,
            ParentId: null, Icon: icon, ColorHex: color, SortOrder: sort, IsArchived: false);
}
