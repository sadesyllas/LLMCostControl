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

