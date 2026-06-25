using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Interface for admin CRUD operations on groups, group budgets, group
/// memberships, and per-user budget overrides (§12.3).
/// </summary>
public interface IAdminCommandService
{
    /// <summary>Creates a new group with the given name.</summary>
    Task<Group> CreateGroupAsync(string name, CancellationToken ct = default);

    /// <summary>Returns all groups.</summary>
    Task<List<Group>> ListGroupsAsync(CancellationToken ct = default);

    /// <summary>Renames an existing group.</summary>
    Task RenameGroupAsync(Guid id, string newName, CancellationToken ct = default);

    /// <summary>Deletes a group by id.</summary>
    Task DeleteGroupAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sets or updates the budget for a group for the current period.</summary>
    Task SetGroupBudgetAsync(Guid groupId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Clears the budget for a group for the current period.</summary>
    Task ClearGroupBudgetAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Returns the budget for a group for the current period, or null.</summary>
    Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Adds a caller to a group.</summary>
    Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    /// <summary>Removes a caller from a group.</summary>
    Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    /// <summary>Returns all memberships for a group.</summary>
    Task<List<GroupMembership>> ListMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Sets or updates a per-user budget override for the current period.</summary>
    Task SetUserOverrideAsync(string callerId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Clears a per-user budget override for the current period.</summary>
    Task ClearUserOverrideAsync(string callerId, CancellationToken ct = default);

    /// <summary>Returns all per-user overrides for the current period.</summary>
    Task<List<UserBudgetOverride>> ListUserOverridesAsync(CancellationToken ct = default);
}
