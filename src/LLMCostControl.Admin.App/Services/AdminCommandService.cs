using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// PostgreSQL-backed <see cref="IAdminCommandService"/>. Creates a short-lived
/// <see cref="CostTrackerDbContext"/> per operation via
/// <c>IDbContextFactory</c> (safe for Blazor Server's long-lived scopes) and
/// drives the shared Infrastructure repositories.
/// </summary>
public sealed class AdminCommandService : IAdminCommandService
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;

    /// <summary>Creates the service with the given context factory.</summary>
    public AdminCommandService(IDbContextFactory<CostTrackerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Group>> ListGroupsAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await new GroupRepository(db).GetAllAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Group> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        var group = Group.Create(name);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new GroupRepository(db).AddAsync(group, ct);
        return group;
    }

    /// <inheritdoc />
    public async Task RenameGroupAsync(Guid groupId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Group name cannot be empty.", nameof(newName));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var repo = new GroupRepository(db);
        var group = await repo.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group '{groupId}' was not found.");

        group.Name = newName.Trim();
        await repo.UpdateAsync(group, ct);
    }

    /// <inheritdoc />
    public async Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new GroupRepository(db).DeleteAsync(groupId, ct);
    }

    /// <inheritdoc />
    public async Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await new GroupBudgetRepository(db).GetAsync(groupId, BudgetPeriod.Current(), ct);
    }

    /// <inheritdoc />
    public async Task SetGroupBudgetAsync(Guid groupId, decimal amount, string currency, CancellationToken ct = default)
    {
        var money = BuildPositiveMoney(amount, currency);
        var budget = GroupBudget.Create(groupId, money, BudgetPeriod.Current());

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new GroupBudgetRepository(db).UpsertAsync(budget, ct);
    }

    /// <inheritdoc />
    public async Task ClearGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new GroupBudgetRepository(db).DeleteAsync(groupId, BudgetPeriod.Current(), ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var callers = await new GroupMembershipRepository(db).GetCallersForGroupAsync(groupId, ct);
        return callers.Select(c => c.Value).ToList();
    }

    /// <inheritdoc />
    public async Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var repo = new GroupMembershipRepository(db);

        if (await repo.ExistsAsync(groupId, caller, ct))
        {
            throw new InvalidOperationException($"Caller '{caller}' is already a member of this group.");
        }

        await repo.AddAsync(GroupMembership.Create(groupId, caller), ct);
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new GroupMembershipRepository(db).RemoveAsync(groupId, caller, ct);
    }

    /// <inheritdoc />
    public async Task<UserBudgetOverride?> GetUserOverrideAsync(string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await new UserBudgetOverrideRepository(db).GetAsync(caller, BudgetPeriod.Current(), ct);
    }

    /// <inheritdoc />
    public async Task SetUserOverrideAsync(string callerId, decimal amount, string currency, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        var money = BuildPositiveMoney(amount, currency);
        var entity = UserBudgetOverride.Create(caller, money, BudgetPeriod.Current());

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new UserBudgetOverrideRepository(db).UpsertAsync(entity, ct);
    }

    /// <inheritdoc />
    public async Task ClearUserOverrideAsync(string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await new UserBudgetOverrideRepository(db).DeleteAsync(caller, BudgetPeriod.Current(), ct);
    }

    /// <summary>
    /// Builds a <see cref="Money"/>, rejecting non-positive amounts (a budget of
    /// zero or less is not a valid budget — §12.3 form validation).
    /// </summary>
    private static Money BuildPositiveMoney(decimal amount, string currency)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("Budget amount must be greater than zero.", nameof(amount));
        }

        return new Money(amount, currency);
    }
}
