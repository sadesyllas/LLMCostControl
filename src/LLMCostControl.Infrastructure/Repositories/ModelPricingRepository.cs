using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="ModelPricing"/> entities: current pricing per
/// model, written by the refresh job and read by the pricing grains.
/// </summary>
public class ModelPricingRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public ModelPricingRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>Gets pricing for a single model by name, or null.</summary>
    public Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default)
        => _db.ModelPricing.FirstOrDefaultAsync(p => p.Model == model, ct);

    /// <summary>Returns all current pricing entries.</summary>
    public Task<List<ModelPricing>> GetAllAsync(CancellationToken ct = default)
        => _db.ModelPricing.ToListAsync(ct);

    /// <summary>Returns all pricing entries for a given provider.</summary>
    public Task<List<ModelPricing>> GetByProviderAsync(Provider provider, CancellationToken ct = default)
        => _db.ModelPricing.Where(p => p.Provider == provider).ToListAsync(ct);

    /// <summary>
    /// Adds or replaces pricing for a model (upsert by model name) and saves.
    /// </summary>
    public async Task UpsertAsync(ModelPricing pricing, CancellationToken ct = default)
    {
        var existing = await _db.ModelPricing
            .FirstOrDefaultAsync(p => p.Model == pricing.Model, ct);

        if (existing is not null)
        {
            _db.ModelPricing.Remove(existing);
        }

        await _db.ModelPricing.AddAsync(pricing, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Replaces all pricing for a provider in a single transaction.</summary>
    public async Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.ModelPricing
            .Where(p => p.Provider == provider)
            .ExecuteDeleteAsync(ct);

        await _db.ModelPricing.AddRangeAsync(entries, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
