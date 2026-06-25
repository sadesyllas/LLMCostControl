using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="UserBudgetOverride"/> entities: explicit per-user
/// budgets that win over group budgets.
/// </summary>
public class UserBudgetOverrideRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public UserBudgetOverrideRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>
    /// Returns the override for a caller and period, or null.
    /// </summary>
    public Task<UserBudgetOverride?> GetAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
        => _db.UserBudgetOverrides
            .FirstOrDefaultAsync(o => o.CallerId == callerId && o.Period == period, ct);

    /// <summary>Adds or replaces a user override and saves.</summary>
    public async Task UpsertAsync(UserBudgetOverride overrideEntity, CancellationToken ct = default)
    {
        var existing = await _db.UserBudgetOverrides
            .FirstOrDefaultAsync(o => o.CallerId == overrideEntity.CallerId
                                   && o.Period == overrideEntity.Period, ct);

        if (existing is not null)
        {
            existing.Amount = overrideEntity.Amount;
        }
        else
        {
            await _db.UserBudgetOverrides.AddAsync(overrideEntity, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a user override and saves.</summary>
    public async Task DeleteAsync(CallerId callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        await _db.UserBudgetOverrides
            .Where(o => o.CallerId == callerId && o.Period == period)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Returns all overrides for a specific period.</summary>
    public Task<List<UserBudgetOverride>> GetForPeriodAsync(BudgetPeriod period, CancellationToken ct = default)
        => _db.UserBudgetOverrides
            .Where(o => o.Period == period)
            .ToListAsync(ct);
}
