using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Application service for admin CRUD operations on groups, group budgets,
/// group membership, and per-user budget overrides (§12.3). All writes go
/// through the shared repository classes from <c>LLMCostControl.Infrastructure</c>.
/// </summary>
public interface IGroupAdminService
{
    // ── Groups ─────────────────────────────────────────────────────────────────

    /// <summary>Returns all groups, ordered by creation date.</summary>
    Task<IReadOnlyList<Group>> GetGroupsAsync(CancellationToken ct = default);

    /// <summary>Creates a new group with the given name and returns it.</summary>
    /// <exception cref="ArgumentException">When <paramref name="name"/> is empty.</exception>
    Task<Group> CreateGroupAsync(string name, CancellationToken ct = default);

    /// <summary>Renames a group.</summary>
    /// <exception cref="ArgumentException">When <paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">When the group does not exist.</exception>
    Task RenameGroupAsync(Guid groupId, string name, CancellationToken ct = default);

    /// <summary>Deletes a group and all its memberships and budgets.</summary>
    Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default);

    // ── Group budgets ──────────────────────────────────────────────────────────

    /// <summary>Returns the budget for a group in the given period, or null.</summary>
    Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default);

    /// <summary>Sets (or replaces) the budget amount for a group in the given period.</summary>
    /// <exception cref="ArgumentException">When <paramref name="amount"/> is negative or zero.</exception>
    Task SetGroupBudgetAsync(Guid groupId, decimal amount, string currency, BudgetPeriod period, CancellationToken ct = default);

    /// <summary>Clears the budget for a group in the given period.</summary>
    Task ClearGroupBudgetAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default);

    // ── Group membership ───────────────────────────────────────────────────────

    /// <summary>Returns all memberships for the given group.</summary>
    Task<IReadOnlyList<GroupMembership>> GetGroupMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Adds a caller to a group. Duplicate membership (same group + caller) is
    /// silently ignored.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="callerId"/> is empty.</exception>
    Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    /// <summary>Removes a caller from a group.</summary>
    Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    // ── Per-user budget overrides ──────────────────────────────────────────────

    /// <summary>Returns the override for a caller in the given period, or null.</summary>
    Task<UserBudgetOverride?> GetUserOverrideAsync(string callerId, BudgetPeriod period, CancellationToken ct = default);

    /// <summary>Sets (or replaces) a per-user budget override.</summary>
    /// <exception cref="ArgumentException">When <paramref name="amount"/> is negative or zero.</exception>
    Task SetUserOverrideAsync(string callerId, decimal amount, string currency, BudgetPeriod period, CancellationToken ct = default);

    /// <summary>Clears the per-user budget override for the given period.</summary>
    Task ClearUserOverrideAsync(string callerId, BudgetPeriod period, CancellationToken ct = default);
}
