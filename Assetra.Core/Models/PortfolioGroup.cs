namespace Assetra.Core.Models;

/// <summary>
/// Portfolio Group (群組 / 投資 bucket) — see <c>docs/planning/Portfolio-Groups-Refactor.md</c>.
///
/// <para>
/// A user-defined bucket that aggregates multiple trades / positions / cash accounts
/// for a single purpose. Examples: 退休帳戶 / 買房儲蓄 / 子女教育金 / 短期投機.
/// Goals and FIRE plans link to a group so progress auto-tracks the group's
/// real-time net value — no manual <see cref="FinancialGoal.CurrentAmount"/> updates.
/// </para>
///
/// <para><b>Why the name <c>PortfolioGroup</c></b>: the existing <c>portfolio</c> table
/// (and <see cref="PortfolioEntry"/> record) already represents individual stock
/// position lots. The new「群組」abstraction sits one layer above and must avoid
/// the name collision. DB table for this entity is <c>portfolio_group</c>; the
/// FK column on related tables is <c>portfolio_group_id</c>.</para>
///
/// <para>
/// <b>Default group</b>: the migrator seeds one row with stable Guid
/// <c>00000000-0000-0000-0000-000000000001</c> (<see cref="DefaultId"/>). All
/// pre-existing trades / cash accounts are reassigned to it. The default group
/// is system-protected — can rename, cannot delete (<see cref="IsSystem"/>=true).
/// </para>
/// </summary>
public sealed record PortfolioGroup(
    Guid Id,
    string Name,
    /// <summary>顯色 hex（如 "#3B82F6"）。UI 用此色 tint 群組相關 chip / segment。null = 主題預設色。</summary>
    string? ColorHex = null,
    /// <summary>選填說明，幫助使用者記憶這群組存的是什麼。</summary>
    string? Description = null,
    /// <summary>Fluent icon symbol key (如 "PersonClock24" 對應 FluentIcons.Common.Symbol)，null = 主題預設 icon。</summary>
    string? IconKey = null,
    /// <summary>排序顯示用，小者在前；同值依 CreatedAt 排序。</summary>
    int SortOrder = 0,
    /// <summary>選填預設扣款 / 入款 cash account。設定後 Buy/Sell dialog 選此群組時自動帶入。</summary>
    Guid? DefaultCashAccountId = null,
    /// <summary>System-protected = true 時 UI 禁止刪除（只能 rename）。Default group 用。</summary>
    bool IsSystem = false)
{
    /// <summary>
    /// Stable seed Guid for the「預設群組」row inserted at first migration.
    /// Existing trades / cash accounts that lacked a group are reassigned here.
    /// </summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Show Name in WPF ComboBox SelectionBoxItem fallback (FormComboBox 用
    /// SelectionBoxItemTemplate，DisplayMemberPath 在某些 binding 階段 race condition
    /// 下仍會掉回 ToString。直接 override 確保 UI 永遠顯示群組名稱而非 record dump).
    /// </summary>
    public override string ToString() => Name;
}
