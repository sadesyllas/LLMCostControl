using LLMCostControl.Domain.Budgets;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Admin command handlers (§12.3) for managing groups, group budgets,
/// membership, and per-user budget overrides. All writes go through the shared
/// <c>LLMCostControl.Infrastructure</c> repositories (DRY); the admin app never
/// calls Orleans grains. Budget operations act on the current budget period.
/// </summary>
public interface IAdminCommandService
{
    /// <summary>Lists all groups.</summary>
    Task<IReadOnlyList<Group>> ListGroupsAsync(CancellationToken ct = default);

    /// <summary>Creates a group with the given name. Throws when the name is empty.</summary>
    Task<Group> CreateGroupAsync(string name, CancellationToken ct = default);

    /// <summary>Renames an existing group. Throws when the name is empty or the group is unknown.</summary>
    Task RenameGroupAsync(Guid groupId, string newName, CancellationToken ct = default);

    /// <summary>Deletes a group.</summary>
    Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Gets the group's budget for the current period, or null.</summary>
    Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Sets (or updates) the group's budget for the current period. Throws when
    /// the amount is not greater than zero or the currency is invalid.
    /// </summary>
    Task SetGroupBudgetAsync(Guid groupId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Clears the group's budget for the current period.</summary>
    Task ClearGroupBudgetAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Lists the caller ids that are members of the group.</summary>
    Task<IReadOnlyList<string>> ListMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Adds a caller to the group. Throws when the caller id is empty or the
    /// caller is already a member (duplicate membership).
    /// </summary>
    Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    /// <summary>Removes a caller from the group.</summary>
    Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default);

    /// <summary>Gets the per-user budget override for a caller for the current period, or null.</summary>
    Task<UserBudgetOverride?> GetUserOverrideAsync(string callerId, CancellationToken ct = default);

    /// <summary>
    /// Sets (or updates) a per-user budget override for the current period. Throws
    /// when the caller id is empty, the amount is not greater than zero, or the
    /// currency is invalid.
    /// </summary>
    Task SetUserOverrideAsync(string callerId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Clears a caller's per-user budget override for the current period.</summary>
    Task ClearUserOverrideAsync(string callerId, CancellationToken ct = default);
}
