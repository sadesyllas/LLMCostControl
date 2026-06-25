using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Interface for admin operations on groups, budgets, overrides, pricing, and
/// read-only views (§12.3). All writes go through the shared repositories.
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

    // ── Read-only views (§12.3) ──

    /// <summary>Resolves the effective budget for a caller for the current period.</summary>
    Task<EffectiveBudget> GetEffectiveBudgetAsync(string callerId, CancellationToken ct = default);

    /// <summary>Computes the running spend for a caller for the current period
    /// from the append-only usage events ledger.</summary>
    Task<decimal> GetRunningSpendAsync(string callerId, CancellationToken ct = default);

    /// <summary>Returns all current pricing entries, ordered by provider then model.</summary>
    Task<List<ModelPricing>> ListPricingAsync(CancellationToken ct = default);

    /// <summary>Returns recent usage events for a caller (most recent first).</summary>
    Task<List<UsageEvent>> ListUsageEventsAsync(string callerId, int limit = 50, CancellationToken ct = default);

    // ── Pricing file upload (§12.3, §8.3) ──

    /// <summary>
    /// Validates and imports a canonical pricing file (§8.5). Uses the shared
    /// <see cref="PricingFileImporter"/> from Infrastructure (same code path as
    /// the tracker's localhost import endpoint, M14).
    /// </summary>
    Task<PricingFileImportResult> ImportPricingFileAsync(string json, CancellationToken ct = default);
}
