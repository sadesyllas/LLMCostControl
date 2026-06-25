using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Production implementation of <see cref="IGroupAdminService"/> that delegates
/// all reads and writes to the shared repository classes.
/// </summary>
public sealed class GroupAdminService : IGroupAdminService
{
    private readonly GroupRepository _groups;
    private readonly GroupMembershipRepository _memberships;
    private readonly GroupBudgetRepository _budgets;
    private readonly UserBudgetOverrideRepository _overrides;

    /// <summary>Creates the service with the required repositories.</summary>
    public GroupAdminService(
        GroupRepository groups,
        GroupMembershipRepository memberships,
        GroupBudgetRepository budgets,
        UserBudgetOverrideRepository overrides)
    {
        _groups = groups;
        _memberships = memberships;
        _budgets = budgets;
        _overrides = overrides;
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<Group>> GetGroupsAsync(CancellationToken ct = default)
        => await _groups.GetAllAsync(ct);

    /// <inheritdoc />
    public async Task<Group> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        var group = Group.Create(name);
        await _groups.AddAsync(group, ct);
        return group;
    }

    /// <inheritdoc />
    public async Task RenameGroupAsync(Guid groupId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name cannot be empty.", nameof(name));

        var group = await _groups.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        group.Name = name.Trim();
        await _groups.UpdateAsync(group, ct);
    }

    /// <inheritdoc />
    public Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
        => _groups.DeleteAsync(groupId, ct);

    // ── Group budgets ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default)
        => _budgets.GetAsync(groupId, period, ct);

    /// <inheritdoc />
    public async Task SetGroupBudgetAsync(
        Guid groupId,
        decimal amount,
        string currency,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        if (amount <= 0m)
            throw new ArgumentException("Budget amount must be greater than zero.", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        var budget = GroupBudget.Create(groupId, new Money(amount, currency), period);
        await _budgets.UpsertAsync(budget, ct);
    }

    /// <inheritdoc />
    public Task ClearGroupBudgetAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default)
        => _budgets.DeleteAsync(groupId, period, ct);

    // ── Group membership ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupMembership>> GetGroupMembersAsync(Guid groupId, CancellationToken ct = default)
        => await _memberships.GetMembershipsForGroupAsync(groupId, ct);

    /// <inheritdoc />
    public async Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId))
            throw new ArgumentException("Caller id cannot be empty.", nameof(callerId));

        var id = CallerId.From(callerId.Trim());

        // Idempotent: skip if already a member.
        var existing = await _memberships.GetMembershipsForGroupAsync(groupId, ct);
        if (existing.Any(m => m.CallerId == id))
            return;

        var membership = GroupMembership.Create(groupId, id);
        await _memberships.AddAsync(membership, ct);
    }

    /// <inheritdoc />
    public Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
        => _memberships.RemoveAsync(groupId, CallerId.From(callerId.Trim()), ct);

    // ── Per-user overrides ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<UserBudgetOverride?> GetUserOverrideAsync(string callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return null;
        return await _overrides.GetAsync(CallerId.From(callerId.Trim()), period, ct);
    }

    /// <inheritdoc />
    public async Task SetUserOverrideAsync(
        string callerId,
        decimal amount,
        string currency,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId))
            throw new ArgumentException("Caller id cannot be empty.", nameof(callerId));
        if (amount <= 0m)
            throw new ArgumentException("Override amount must be greater than zero.", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        var id = CallerId.From(callerId.Trim());
        var entity = UserBudgetOverride.Create(id, new Money(amount, currency), period);
        await _overrides.UpsertAsync(entity, ct);
    }

    /// <inheritdoc />
    public Task ClearUserOverrideAsync(string callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return Task.CompletedTask;
        return _overrides.DeleteAsync(CallerId.From(callerId.Trim()), period, ct);
    }
}
