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
        // Icon 採 Fluent System Icons symbol name（與 navrail / dialog 風格一致），
        // 由 ds:AppIcon 控件負責解析渲染。
        yield return New("飲食", CategoryKind.Expense, ++sort, "FoodToast24", "#F59E0B");
        yield return New("交通", CategoryKind.Expense, ++sort, "VehicleSubway24", "#3B82F6");
        yield return New("居住", CategoryKind.Expense, ++sort, "Home24", "#8B5CF6");
        yield return New("水電瓦斯", CategoryKind.Expense, ++sort, "Lightbulb24", "#06B6D4");
        yield return New("通訊", CategoryKind.Expense, ++sort, "Phone24", "#0EA5E9");
        yield return New("購物", CategoryKind.Expense, ++sort, "ShoppingBag24", "#EC4899");
        yield return New("娛樂", CategoryKind.Expense, ++sort, "Filmstrip24", "#A855F7");
        yield return New("醫療", CategoryKind.Expense, ++sort, "Stethoscope24", "#EF4444");
        yield return New("教育", CategoryKind.Expense, ++sort, "BookOpen24", "#10B981");
        yield return New("保險", CategoryKind.Expense, ++sort, "ShieldCheckmark24", "#64748B");
        yield return New("訂閱服務", CategoryKind.Expense, ++sort, "ArrowSync24", "#F97316");
        yield return New("其他支出", CategoryKind.Expense, ++sort, "MoneyDismiss24", "#9CA3AF");

        // ── 收入 ─────────────────────────────────────────────────────
        yield return New("薪資", CategoryKind.Income, ++sort, "Briefcase24", "#22C55E");
        yield return New("獎金", CategoryKind.Income, ++sort, "Gift24", "#EAB308");
        yield return New("利息", CategoryKind.Income, ++sort, "BuildingBank24", "#14B8A6");
        yield return New("退稅", CategoryKind.Income, ++sort, "Receipt24", "#84CC16");
        yield return New("其他收入", CategoryKind.Income, ++sort, "Money24", "#6B7280");
    }

    private static ExpenseCategory New(string name, CategoryKind kind, int sort, string icon, string color) =>
        new(Id: Guid.NewGuid(), Name: name, Kind: kind,
            ParentId: null, Icon: icon, ColorHex: color, SortOrder: sort, IsArchived: false);
}
