using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// PostgreSQL-backed <see cref="IPricingWriter"/> that persists pricing via
/// <see cref="ModelPricingRepository.ReplaceProviderPricingAsync"/> — the exact
/// write path used by the refresh job (§8.4). Uses an
/// <c>IDbContextFactory</c> (like <c>PricingStore</c>) so it is safe to resolve
/// as a singleton.
/// </summary>
public sealed class DbPricingWriter : IPricingWriter
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;

    /// <summary>Creates the writer with the given context factory.</summary>
    public DbPricingWriter(IDbContextFactory<CostTrackerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var repository = new ModelPricingRepository(context);
        await repository.ReplaceProviderPricingAsync(provider, entries, ct);
    }
}
