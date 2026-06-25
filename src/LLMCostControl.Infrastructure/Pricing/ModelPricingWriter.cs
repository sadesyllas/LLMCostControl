using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Production implementation of <see cref="IPricingStoreWriter"/> that
/// persists pricing via <see cref="ModelPricingRepository"/> using a fresh
/// <see cref="CostTrackerDbContext"/> per operation, making it safe for
/// singleton lifetime.
/// </summary>
public sealed class ModelPricingWriter : IPricingStoreWriter
{
    private readonly IDbContextFactory<CostTrackerDbContext> _factory;

    /// <summary>Creates the writer with the given DbContext factory.</summary>
    public ModelPricingWriter(IDbContextFactory<CostTrackerDbContext> factory)
        => _factory = factory;

    /// <inheritdoc />
    public async Task ReplaceProviderAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var repo = new ModelPricingRepository(db);
        await repo.ReplaceProviderPricingAsync(provider, entries, ct);
    }
}
