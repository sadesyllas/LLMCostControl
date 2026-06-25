using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Read-side interface for resolving the effective budget of a caller from the
/// database inside a grain. Grains cannot use scoped <c>DbContext</c> directly,
/// so this abstraction wraps a <c>IDbContextFactory</c> behind a transient-safe
/// interface, mirroring <see cref="IPricingStore"/>.
/// </summary>
public interface IBudgetStore
{
    /// <summary>
    /// Resolves the effective budget for the given caller in the given period:
    /// per-user override wins; otherwise the largest group budget; otherwise
    /// none (§7).
    /// </summary>
    Task<EffectiveBudget> ResolveAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of <see cref="IBudgetStore"/> that reads budget data from
/// PostgreSQL via a <c>IDbContextFactory</c>, safe for use inside grains
/// (which are singleton-scoped and cannot use scoped <c>DbContext</c>).
/// </summary>
public sealed class BudgetStore : IBudgetStore
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;

    /// <summary>Creates the store with the given context factory.</summary>
    public BudgetStore(IDbContextFactory<CostTrackerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Resolves the effective budget for the given caller and period.
    /// </summary>
    public async Task<EffectiveBudget> ResolveAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var repo = new BudgetResolutionRepository(context);
        return await repo.ResolveAsync(callerId, period, ct);
    }
}
