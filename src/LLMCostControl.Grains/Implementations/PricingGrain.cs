using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using LLMCostControl.Grains.Storage;
using Orleans;
using Orleans.Concurrency;
using Orleans.Streams;

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
public sealed class PricingGrain : Grain, IPricingGrain, IAsyncObserver<PricingUpdatedStreamEvent>
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
    /// Subscribes to the pricing-updated stream to receive pushed pricing changes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var streamProvider = this.GetStreamProvider("pricing");
        var stream = streamProvider.GetStream<PricingUpdatedStreamEvent>("pricing", "updates");
        await stream.SubscribeAsync(this);
        await base.OnActivateAsync(cancellationToken);
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

    /// <summary>
    /// Invoked when a pricing update event is pushed onto the stream.
    /// Clears the local silo cache for the model if it matches this grain's key.
    /// </summary>
    /// <param name="item">The pricing update event.</param>
    /// <param name="token">The stream sequence token.</param>
    Task IAsyncObserver<PricingUpdatedStreamEvent>.OnNextAsync(PricingUpdatedStreamEvent item, StreamSequenceToken? token)
    {
        var modelName = this.GetPrimaryKeyString();
        if (item.UpdatedModels.Contains(modelName, StringComparer.OrdinalIgnoreCase))
        {
            _cache.Remove(modelName);
        }
        return Task.CompletedTask;
    }

    /// <summary>Called when the stream completes.</summary>
    Task IAsyncObserver<PricingUpdatedStreamEvent>.OnCompletedAsync() => Task.CompletedTask;

    /// <summary>Called when the stream has an error.</summary>
    /// <param name="ex">The stream error exception.</param>
    Task IAsyncObserver<PricingUpdatedStreamEvent>.OnErrorAsync(Exception ex) => Task.CompletedTask;
}
