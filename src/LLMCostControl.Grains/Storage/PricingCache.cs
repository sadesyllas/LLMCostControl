using LLMCostControl.Grains.Abstractions;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// A thread-safe, per-silo in-memory cache of model pricing. Shared by all
/// <c>[StatelessWorker]</c> <c>PricingGrain</c> activations on the same silo,
/// so only one DB read per model per TTL window is needed.
/// </summary>
public interface IPricingCache
{
    /// <summary>
    /// Returns the cached pricing for a model, or null when not cached / expired.
    /// Does NOT load from the store — caller should check and call
    /// <see cref="RefreshAsync"/> when null.
    /// </summary>
    PricingResult? Get(string model);

    /// <summary>
    /// Loads (or reloads) the pricing for a model from the store and caches it.
    /// </summary>
    Task RefreshAsync(string model, IPricingStore store, CancellationToken ct = default);

    /// <summary>Removes a model from the cache.</summary>
    void Remove(string model);
}

/// <summary>
/// Singleton <see cref="IPricingCache"/> backed by a
/// <c>ConcurrentDictionary</c> with a TTL per entry.
/// </summary>
public sealed class PricingCache : IPricingCache
{
    private readonly TimeSpan _ttl;
    private readonly Dictionary<string, (PricingResult Result, DateTimeOffset LoadedAt)> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>
    /// Creates the cache with the given TTL (default 30 s).
    /// </summary>
    public PricingCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Returns the cached pricing if present and not expired, otherwise null.
    /// </summary>
    public PricingResult? Get(string model)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(model, out var entry))
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - entry.LoadedAt > _ttl)
            {
                _entries.Remove(model);
                return null;
            }

            return entry.Result;
        }
    }

    /// <summary>
    /// Loads pricing from the store and caches it with the current timestamp.
    /// </summary>
    public async Task RefreshAsync(string model, IPricingStore store, CancellationToken ct = default)
    {
        var pricing = await store.GetByModelAsync(model, ct);

        var result = pricing is null ? null : new PricingResult
        {
            Input = pricing.Prices.Input,
            Output = pricing.Prices.Output,
            CacheRead = pricing.Prices.CacheRead,
            CacheWrite = pricing.Prices.CacheWrite,
            Currency = pricing.Currency,
            IsStale = pricing.IsStale,
        };

        lock (_lock)
        {
            if (result is null)
            {
                _entries.Remove(model);
            }
            else
            {
                _entries[model] = (result, DateTimeOffset.UtcNow);
            }
        }
    }

    /// <summary>Removes a model from the cache.</summary>
    public void Remove(string model)
    {
        lock (_lock)
        {
            _entries.Remove(model);
        }
    }
}
