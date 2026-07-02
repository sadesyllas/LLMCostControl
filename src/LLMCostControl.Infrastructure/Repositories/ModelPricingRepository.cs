using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="ModelPricing"/> entities: versioned pricing history per
/// model, written by the refresh job / admin upload and read by the pricing grains.
/// </summary>
public class ModelPricingRepository
{
    private readonly CostTrackerDbContext _db;
    private readonly PricingRefreshOptions? _options;

    /// <summary>Creates the repository with the given DbContext and options.</summary>
    public ModelPricingRepository(CostTrackerDbContext db, PricingRefreshOptions? options = null)
    {
        _db = db;
        _options = options;
    }

    private ModelPricing? ApplyDynamicStaleness(ModelPricing? pricing)
    {
        if (pricing is null)
        {
            return null;
        }

        var cadence = _options?.GetCadence(pricing.Provider) ?? TimeSpan.FromHours(1);

        if (pricing.StaleSince is null && DateTimeOffset.UtcNow - pricing.FetchedAt > cadence)
        {
            pricing.StaleSince = pricing.FetchedAt + cadence;
        }

        return pricing;
    }

    /// <summary>Gets the latest pricing version for a single model by name, or null.</summary>
    public async Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default)
    {
        var res = await _db.ModelPricing
            .Where(p => p.Model == model)
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Id)
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
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);
        return ApplyDynamicStaleness(res);
    }

    /// <summary>Returns the latest pricing version for all models.</summary>
    public async Task<List<ModelPricing>> GetAllAsync(CancellationToken ct = default)
    {
        var filtered = await _db.ModelPricing
            .GroupBy(p => new { p.Provider, p.Model })
            .Select(g => g.OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id).First())
            .ToListAsync(ct);

        foreach (var item in filtered)
        {
            ApplyDynamicStaleness(item);
        }

        return filtered;
    }

    /// <summary>Returns the latest pricing version for all models under a given provider.</summary>
    public async Task<List<ModelPricing>> GetByProviderAsync(Provider provider, CancellationToken ct = default)
    {
        var filtered = await _db.ModelPricing
            .Where(p => p.Provider == provider)
            .GroupBy(p => p.Model)
            .Select(g => g.OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id).First())
            .ToListAsync(ct);

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
        var (id, _) = await InsertNewVersionInternalAsync(pricing, ct);
        return id;
    }

    /// <summary>
    /// Inserts a new pricing version using insert-on-change logic internally.
    /// Reports whether a new database row was actually inserted.
    /// </summary>
    public async Task<(Guid Id, bool Inserted)> InsertNewVersionInternalAsync(ModelPricing pricing, CancellationToken ct = default)
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
            return (latest.Id, false);
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
        return (newVersion.Id, true);
    }

    /// <summary>Replaces provider pricing atomically using insert-on-change. Returns a map of updated model names to their new version IDs.</summary>
    public async Task<IReadOnlyDictionary<string, Guid>> ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        var changed = new Dictionary<string, Guid>();
        foreach (var entry in entries)
        {
            var (id, inserted) = await InsertNewVersionInternalAsync(entry, ct);
            if (inserted)
            {
                changed[entry.Model] = id;
            }
        }
        return changed;
    }

    /// <summary>Replaces multiple providers pricing atomically using insert-on-change. Returns a map of updated model names to their new version IDs.</summary>
    public async Task<IReadOnlyDictionary<string, Guid>> ReplaceMultipleProvidersPricingAsync(
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        var changed = new Dictionary<string, Guid>();
        foreach (var entry in entries)
        {
            var (id, inserted) = await InsertNewVersionInternalAsync(entry, ct);
            if (inserted)
            {
                changed[entry.Model] = id;
            }
        }
        return changed;
    }
}
