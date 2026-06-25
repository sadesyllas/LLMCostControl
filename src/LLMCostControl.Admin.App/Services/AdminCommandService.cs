using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Service for admin CRUD operations on groups, group budgets, group
/// memberships, and per-user budget overrides (§12.3). All writes go through
/// the shared repositories from <see cref="LLMCostControl.Infrastructure"/>.
/// </summary>
public sealed class AdminCommandService : IAdminCommandService
{
    private readonly IDbContextFactory<CostTrackerDbContext> _dbContextFactory;
    private readonly PricingFileImporter _pricingFileImporter;

    /// <summary>Creates the service with the given DbContext factory and pricing file importer.</summary>
    public AdminCommandService(
        IDbContextFactory<CostTrackerDbContext> dbContextFactory,
        PricingFileImporter pricingFileImporter)
    {
        _dbContextFactory = dbContextFactory;
        _pricingFileImporter = pricingFileImporter;
    }

    // ── Groups ──

    /// <summary>Creates a new group with the given name.</summary>
    public async Task<Group> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupRepository(db);
        var group = Group.Create(name);
        await repo.AddAsync(group, ct);
        return group;
    }

    /// <summary>Returns all groups, ordered by name.</summary>
    public async Task<List<Group>> ListGroupsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupRepository(db);
        return await repo.GetAllAsync(ct);
    }

    /// <summary>Renames an existing group.</summary>
    public async Task RenameGroupAsync(Guid id, string newName, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupRepository(db);
        var group = await repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Group {id} not found.");
        group.Name = newName.Trim();
        await repo.UpdateAsync(group, ct);
    }

    /// <summary>Deletes a group by id. Cascading deletes remove associated
    /// budgets and memberships (via DB cascade or explicit cleanup).</summary>
    public async Task DeleteGroupAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupRepository(db);
        await repo.DeleteAsync(id, ct);
    }

    // ── Group budgets ──

    /// <summary>Sets or updates the budget for a group for the current period.</summary>
    public async Task SetGroupBudgetAsync(
        Guid groupId, decimal amount, string currency, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupBudgetRepository(db);
        var budget = GroupBudget.Create(groupId, new Money(amount, currency), BudgetPeriod.Current());
        await repo.UpsertAsync(budget, ct);
    }

    /// <summary>Clears (deletes) the budget for a group for the current period.</summary>
    public async Task ClearGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var period = BudgetPeriod.Current();
        var all = await db.GroupBudgets.ToListAsync(ct);
        var existing = all.FirstOrDefault(b => b.GroupId == groupId && b.Period == period);
        if (existing is not null)
        {
            db.GroupBudgets.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Returns the budget for a group for the current period, or null.</summary>
    public async Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var period = BudgetPeriod.Current();
        var all = await db.GroupBudgets.ToListAsync(ct);
        return all.FirstOrDefault(b => b.GroupId == groupId && b.Period == period);
    }

    // ── Group membership ──

    /// <summary>Adds a caller to a group.</summary>
    public async Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupMembershipRepository(db);
        var membership = GroupMembership.Create(groupId, CallerId.From(callerId));
        await repo.AddAsync(membership, ct);
    }

    /// <summary>Removes a caller from a group.</summary>
    public async Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new GroupMembershipRepository(db);
        await repo.RemoveAsync(groupId, CallerId.From(callerId), ct);
    }

    /// <summary>Returns all memberships for a group.</summary>
    public async Task<List<GroupMembership>> ListMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var members = await db.GroupMemberships
            .Where(m => m.GroupId == groupId)
            .ToListAsync(ct);
        return members.OrderBy(m => m.CallerId.Value).ToList();
    }

    // ── Per-user budget overrides ──

    /// <summary>Sets or updates a per-user budget override for the current period.</summary>
    public async Task SetUserOverrideAsync(
        string callerId, decimal amount, string currency, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new UserBudgetOverrideRepository(db);
        var overrideEntity = UserBudgetOverride.Create(
            CallerId.From(callerId), new Money(amount, currency), BudgetPeriod.Current());
        await repo.UpsertAsync(overrideEntity, ct);
    }

    /// <summary>Clears (deletes) a per-user budget override for the current period.</summary>
    public async Task ClearUserOverrideAsync(string callerId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new UserBudgetOverrideRepository(db);
        await repo.DeleteAsync(CallerId.From(callerId), BudgetPeriod.Current(), ct);
    }

    /// <summary>Returns all per-user overrides for the current period.</summary>
    public async Task<List<UserBudgetOverride>> ListUserOverridesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var period = BudgetPeriod.Current();
        var all = await db.UserBudgetOverrides.ToListAsync(ct);
        return all
            .Where(o => o.Period == period)
            .OrderBy(o => o.CallerId.Value)
            .ToList();
    }

    // ── Read-only views (§12.3) ──

    /// <summary>
    /// Resolves the effective budget for a caller for the current period using
    /// the shared <see cref="BudgetResolutionRepository"/>.
    /// </summary>
    public async Task<EffectiveBudget> GetEffectiveBudgetAsync(string callerId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new BudgetResolutionRepository(db);
        return await repo.ResolveAsync(CallerId.From(callerId), BudgetPeriod.Current(), ct);
    }

    /// <summary>
    /// Computes the running spend for a caller for the current period by
    /// summing <c>cost_amount</c> from the append-only usage events ledger.
    /// </summary>
    public async Task<decimal> GetRunningSpendAsync(string callerId, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var period = BudgetPeriod.Current();
        var caller = CallerId.From(callerId);
        var events = await db.UsageEvents
            .Where(e => e.CallerId == caller)
            .ToListAsync(ct);
        return events
            .Where(e => e.Period == period)
            .Sum(e => e.CostAmount);
    }

    /// <summary>
    /// Returns all current pricing entries, ordered by provider then model.
    /// </summary>
    public async Task<List<ModelPricing>> ListPricingAsync(CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var repo = new ModelPricingRepository(db);
        return await repo.GetAllAsync(ct);
    }

    /// <summary>
    /// Returns recent usage events for a caller (most recent first).
    /// </summary>
    public async Task<List<UsageEvent>> ListUsageEventsAsync(string callerId, int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var caller = CallerId.From(callerId);
        var repo = new UsageEventRepository(db);
        var period = BudgetPeriod.Current();
        var events = await repo.GetForCallerAsync(caller, period, ct);
        return events.TakeLast(limit).Reverse().ToList();
    }

    // ── Pricing file upload (§12.3, §8.3) ──

    /// <summary>
    /// Validates and imports a canonical pricing file (§8.5) via the shared
    /// <see cref="PricingFileImporter"/> (same code path as M14).
    /// </summary>
    public async Task<PricingFileImportResult> ImportPricingFileAsync(string json, CancellationToken ct = default)
    {
        return await _pricingFileImporter.ImportAsync(json, ct);
    }
}
