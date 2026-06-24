using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Resolves the effective budget for a caller by combining group memberships,
/// group budgets, and per-user overrides (per SPEC §7). This is the read-side
/// used by <c>UserBudgetGrain</c> (with its 30 s TTL cache).
/// </summary>
public class BudgetResolutionRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public BudgetResolutionRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>
    /// Resolves the effective budget for a caller in a given period:
    /// per-user override wins; otherwise the largest group budget; otherwise
    /// none.
    /// </summary>
    public async Task<EffectiveBudget> ResolveAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        var userOverride = await _db.UserBudgetOverrides
            .FirstOrDefaultAsync(o => o.CallerId == callerId && o.Period == period, ct);

        if (userOverride is not null)
        {
            return EffectiveBudget.FromUserOverride(userOverride.Amount);
        }

        var groupIds = await _db.GroupMemberships
            .Where(m => m.CallerId == callerId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
        {
            return EffectiveBudget.None();
        }

        var groupBudgets = await _db.GroupBudgets
            .Where(b => groupIds.Contains(b.GroupId) && b.Period == period)
            .ToListAsync(ct);

        if (groupBudgets.Count == 0)
        {
            return EffectiveBudget.None();
        }

        var largest = groupBudgets
            .OrderByDescending(b => b.Amount.Amount)
            .First();

        return EffectiveBudget.FromGroup(largest.Amount, largest.GroupId);
    }
}
