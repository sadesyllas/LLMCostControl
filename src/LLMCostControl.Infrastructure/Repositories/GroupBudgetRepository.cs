using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="GroupBudget"/> entities: per-group, per-period
/// budgets.
/// </summary>
public class GroupBudgetRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public GroupBudgetRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>
    /// Returns the budget for a specific group and period, or null.
    /// </summary>
    public Task<GroupBudget?> GetAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default)
        => _db.GroupBudgets
            .FirstOrDefaultAsync(b => b.GroupId == groupId && b.Period == period, ct);

    /// <summary>
    /// Returns the budgets for all the given groups for the specified period.
    /// </summary>
    public Task<List<GroupBudget>> GetForGroupsAsync(
        IReadOnlyCollection<Guid> groupIds,
        BudgetPeriod period,
        CancellationToken ct = default)
        => _db.GroupBudgets
            .Where(b => groupIds.Contains(b.GroupId) && b.Period == period)
            .ToListAsync(ct);

    /// <summary>Deletes the budget for a group and period, if it exists.</summary>
    public async Task DeleteAsync(Guid groupId, BudgetPeriod period, CancellationToken ct = default)
    {
        await _db.GroupBudgets
            .Where(b => b.GroupId == groupId && b.Period == period)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Adds or replaces a group budget and saves.</summary>
    public async Task UpsertAsync(GroupBudget budget, CancellationToken ct = default)
    {
        var existing = await _db.GroupBudgets
            .FirstOrDefaultAsync(b => b.GroupId == budget.GroupId && b.Period == budget.Period, ct);

        if (existing is not null)
        {
            existing.Amount = budget.Amount;
        }
        else
        {
            await _db.GroupBudgets.AddAsync(budget, ct);
        }

        await _db.SaveChangesAsync(ct);
    }
}
