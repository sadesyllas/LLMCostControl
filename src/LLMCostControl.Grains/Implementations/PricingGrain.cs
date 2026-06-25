using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Storage;
using Orleans;
using Orleans.Concurrency;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Implementation of <see cref="IPricingGrain"/> keyed by model name. Holds
/// the model's current unit prices in a per-silo shared cache so the capture
/// path can compute cost without a DB round-trip on every call (§8.6).
/// <para>
/// Decorated with <c>[StatelessWorker]</c> for local-only activation: the
/// runtime always returns a local activation, never invoking across silos.
/// Multiple activations per silo are allowed for contention-free reads, all
/// sharing the same <see cref="IPricingCache"/>.
/// </para>
/// <para>
/// On first access, the grain loads its pricing from the DB (via
/// <see cref="IPricingStore"/>) into the shared cache. The cache has a 30 s TTL
/// so that admin-side pricing changes become visible without a restart.
/// </para>
/// </summary>
[StatelessWorker]
public sealed class PricingGrain : Grain, IPricingGrain
{
    private readonly IPricingStore _store;
    private readonly IPricingCache _cache;

    /// <summary>
    /// Creates the grain with the given pricing store and shared cache.
    /// </summary>
    public PricingGrain(IPricingStore store, IPricingCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <summary>
    /// Returns the current pricing for this model, or null when unknown. Loads
    /// from the DB on first access (or when the cache TTL has expired).
    /// Read-only on the hot path after the cache is warm.
    /// </summary>
    public async Task<PricingResult?> GetPricingAsync()
    {
        var modelName = this.GetPrimaryKeyString();
        var cached = _cache.Get(modelName);

        if (cached is not null)
        {
            return cached;
        }

        await _cache.RefreshAsync(modelName, _store);
        return _cache.Get(modelName);
    }
}
