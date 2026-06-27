using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Implementation of <see cref="IPricingStore"/> that reads pricing from
/// PostgreSQL via a <c>IDbContextFactory</c>, safe for use inside grains
/// (which are singleton-scoped and cannot use scoped <c>DbContext</c>).
/// </summary>
public sealed class PricingStore : IPricingStore
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;

    /// <summary>Creates the store with the given context factory.</summary>
    public PricingStore(IDbContextFactory<CostTrackerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Returns the current pricing for the given (provider, model) pair, or null.
    /// </summary>
    public async Task<ModelPricing?> GetAsync(Provider provider, string model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var repo = new ModelPricingRepository(context);
        return await repo.GetByProviderAndModelAsync(provider, model, ct);
    }
}
