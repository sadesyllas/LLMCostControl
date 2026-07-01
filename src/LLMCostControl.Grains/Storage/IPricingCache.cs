using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// A thread-safe, per-silo in-memory cache of model pricing, keyed by the
/// composite <c>"{provider}:{model}"</c> key (§8.6). Shared by all
/// <c>[StatelessWorker]</c> <c>PricingGrain</c> activations on the same silo,
/// so only one DB read per (provider, model) per TTL window is needed.
/// </summary>
public interface IPricingCache
{
    /// <summary>
    /// Returns the cached pricing for a composite key, or null when not cached /
    /// expired. Does NOT load from the store — caller should check and call
    /// <see cref="RefreshAsync"/> when null.
    /// </summary>
    PricingResult? Get(string key);

    /// <summary>
    /// Loads (or reloads) the pricing for the given (provider, model) from the
    /// store and caches it under <paramref name="key"/>.
    /// </summary>
    Task RefreshAsync(string key, Provider provider, string model, IPricingStore store, CancellationToken ct = default);

    /// <summary>Removes a composite key from the cache.</summary>
    void Remove(string key);
}
