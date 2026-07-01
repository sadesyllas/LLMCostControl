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
    /// Returns the override for a caller and period type, or null.
    /// </summary>
    public Task<UserBudgetOverride?> GetAsync(
        CallerId callerId,
        BudgetPeriodType periodType,
        CancellationToken ct = default)
        => _db.UserBudgetOverrides
            .FirstOrDefaultAsync(o => o.CallerId == callerId && o.PeriodType == periodType, ct);

    /// <summary>Adds or replaces a user override and saves.</summary>
    public async Task UpsertAsync(UserBudgetOverride overrideEntity, CancellationToken ct = default)
    {
        var existing = await _db.UserBudgetOverrides
            .FirstOrDefaultAsync(o => o.CallerId == overrideEntity.CallerId
                                   && o.PeriodType == overrideEntity.PeriodType, ct);

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
    public async Task DeleteAsync(CallerId callerId, BudgetPeriodType periodType, CancellationToken ct = default)
    {
        await _db.UserBudgetOverrides
            .Where(o => o.CallerId == callerId && o.PeriodType == periodType)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Returns all overrides for a specific period type.</summary>
    public Task<List<UserBudgetOverride>> GetForPeriodTypeAsync(BudgetPeriodType periodType, CancellationToken ct = default)
        => _db.UserBudgetOverrides
            .Where(o => o.PeriodType == periodType)
            .ToListAsync(ct);

    /// <summary>Returns distinct caller IDs across all overrides.</summary>
    public Task<List<CallerId>> GetDistinctCallersAsync(CancellationToken ct = default)
        => _db.UserBudgetOverrides
            .Select(o => o.CallerId)
            .Distinct()
            .ToListAsync(ct);
}
