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
    public PricingResult? Get(string key)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - entry.LoadedAt > _ttl)
            {
                _entries.Remove(key);
                return null;
            }

            return entry.Result;
        }
    }

    /// <summary>
    /// Loads pricing for the (provider, model) from the store and caches it under
    /// <paramref name="key"/> with the current timestamp.
    /// </summary>
    public async Task RefreshAsync(string key, Provider provider, string model, IPricingStore store, CancellationToken ct = default)
    {
        var pricing = await store.GetAsync(provider, model, ct);

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
                _entries.Remove(key);
            }
            else
            {
                _entries[key] = (result, DateTimeOffset.UtcNow);
            }
        }
    }

    /// <summary>Removes a composite key from the cache.</summary>
    public void Remove(string key)
    {
        lock (_lock)
        {
            _entries.Remove(key);
        }
    }
}
