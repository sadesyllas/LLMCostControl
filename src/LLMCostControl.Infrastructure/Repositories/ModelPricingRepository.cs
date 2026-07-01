using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="ModelPricing"/> entities: versioned pricing history per
/// model, written by the refresh job / admin upload and read by the pricing grains.
/// </summary>
public class ModelPricingRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public ModelPricingRepository(CostTrackerDbContext db) => _db = db;

    private static ModelPricing? ApplyDynamicStaleness(ModelPricing? pricing)
    {
        if (pricing is null)
        {
            return null;
        }

        if (pricing.StaleSince is null && DateTimeOffset.UtcNow - pricing.FetchedAt > TimeSpan.FromHours(1))
        {
            pricing.StaleSince = pricing.FetchedAt + TimeSpan.FromHours(1);
        }

        return pricing;
    }

    /// <summary>Gets the latest pricing version for a single model by name, or null.</summary>
    public async Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default)
    {
        var res = await _db.ModelPricing
            .Where(p => p.Model == model)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return ApplyDynamicStaleness(res);
    }

    /// <summary>
    /// Gets the latest pricing version for a single (provider, model) pair, or null.
    /// </summary>
    public async Task<ModelPricing?> GetByProviderAndModelAsync(Provider provider, string model, CancellationToken ct = default)
    {
        var res = await _db.ModelPricing
            .Where(p => p.Provider == provider && p.Model == model)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return ApplyDynamicStaleness(res);
    }

    /// <summary>Returns the latest pricing version for all models.</summary>
    public async Task<List<ModelPricing>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await _db.ModelPricing.ToListAsync(ct);
        var filtered = all
            .GroupBy(p => new { p.Provider, p.Model })
            .Select(g => g.OrderByDescending(p => p.EffectiveFrom).First())
            .ToList();

        foreach (var item in filtered)
        {
            ApplyDynamicStaleness(item);
        }

        return filtered;
    }

    /// <summary>Returns the latest pricing version for all models under a given provider.</summary>
    public async Task<List<ModelPricing>> GetByProviderAsync(Provider provider, CancellationToken ct = default)
    {
        var all = await _db.ModelPricing.Where(p => p.Provider == provider).ToListAsync(ct);
        var filtered = all
            .GroupBy(p => p.Model)
            .Select(g => g.OrderByDescending(p => p.EffectiveFrom).First())
            .ToList();

        foreach (var item in filtered)
        {
            ApplyDynamicStaleness(item);
        }

        return filtered;
    }

    /// <summary>
    /// Inserts a new pricing version using insert-on-change logic.
    /// If the latest version for the same provider and model has identical pricing,
    /// it is a no-op and returns the existing version's ID. Otherwise, it generates
    /// a new version and inserts it.
    /// </summary>
    public async Task<Guid> InsertNewVersionAsync(ModelPricing pricing, CancellationToken ct = default)
    {
        var latest = await GetByProviderAndModelAsync(pricing.Provider, pricing.Model, ct);

        if (latest is not null &&
            latest.Prices.Input == pricing.Prices.Input &&
            latest.Prices.Output == pricing.Prices.Output &&
            latest.Prices.CacheRead == pricing.Prices.CacheRead &&
            latest.Prices.CacheWrite == pricing.Prices.CacheWrite &&
            string.Equals(latest.Currency, pricing.Currency, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(latest.Unit, pricing.Unit, StringComparison.OrdinalIgnoreCase))
        {
            return latest.Id;
        }

        var newVersion = ModelPricing.Create(
            pricing.Provider,
            pricing.Model,
            pricing.Prices,
            pricing.Currency,
            pricing.Unit,
            pricing.FetchedAt,
            pricing.EffectiveFrom != default ? pricing.EffectiveFrom : DateTimeOffset.UtcNow);

        await _db.ModelPricing.AddAsync(newVersion, ct);
        await _db.SaveChangesAsync(ct);
        return newVersion.Id;
    }

    /// <summary>Replaces provider pricing atomically using insert-on-change.</summary>
    public async Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            await InsertNewVersionAsync(entry, ct);
        }
    }

    /// <summary>Replaces multiple providers pricing atomically using insert-on-change.</summary>
    public async Task ReplaceMultipleProvidersPricingAsync(
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            await InsertNewVersionAsync(entry, ct);
        }
    }
}
