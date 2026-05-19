using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// CRUD for <see cref="PortfolioGroup"/> rows. Mirrors the shape of other CRUD repos
/// in this project (e.g. <c>IFinancialGoalRepository</c>): all methods async with
/// CancellationToken; returns immutable collections.
///
/// <para>
/// Implementations must enforce <see cref="PortfolioGroup.IsSystem"/> protection:
/// <see cref="RemoveAsync"/> on a system-protected row throws
/// <see cref="InvalidOperationException"/> with a friendly message.
/// </para>
/// </summary>
public interface IPortfolioGroupRepository
{
    Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default);

    Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new group. <paramref name="group"/>.<see cref="PortfolioGroup.Id"/>
    /// must be a fresh Guid (caller assigns); duplicate Id throws.
    /// </summary>
    Task AddAsync(PortfolioGroup group, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing group. System-protected rows can be renamed (Name /
    /// Description / Color / Icon / SortOrder / DefaultCashAccountId), but
    /// <see cref="PortfolioGroup.IsSystem"/> itself cannot be flipped via this path.
    /// </summary>
    Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a group. Throws if the row is system-protected. Caller is
    /// responsible for re-assigning dependent trades / cash accounts before
    /// invoking — the repo does NOT cascade.
    /// </summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
