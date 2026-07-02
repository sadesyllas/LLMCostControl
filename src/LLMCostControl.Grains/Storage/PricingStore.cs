using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Implementation of <see cref="IPricingStore"/> that reads pricing from
/// PostgreSQL via a <c>IDbContextFactory</c>, safe for use inside grains
/// (which are singleton-scoped and cannot use scoped <c>DbContext</c>).
/// </summary>
public sealed class PricingStore : IPricingStore
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;
    private readonly PricingRefreshOptions? _options;

    /// <summary>Creates the store with the given context factory and optional pricing options.</summary>
    public PricingStore(
        IDbContextFactory<CostTrackerDbContext> contextFactory,
        IOptions<PricingRefreshOptions>? options = null)
    {
        _contextFactory = contextFactory;
        _options = options?.Value;
    }

    /// <summary>
    /// Returns the current pricing for the given (provider, model) pair, or null.
    /// </summary>
    public async Task<ModelPricing?> GetAsync(Provider provider, string model, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var repo = new ModelPricingRepository(context, _options);
        return await repo.GetByProviderAndModelAsync(provider, model, ct);
    }
}
