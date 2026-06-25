using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using LLMCostControl.Grains.Storage;
using Orleans;
using Orleans.Concurrency;
using Orleans.Streams;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Implementation of <see cref="IPricingGrain"/> keyed by model name. Holds
/// the model's current unit prices in-memory so the capture path can compute
/// cost without a DB round-trip on every call (§8.6).
/// <para>
/// Decorated with <c>[StatelessWorker]</c> for local-only activation: the
/// runtime always returns a local activation, never invoking across silos.
/// Multiple activations per silo are allowed for contention-free reads.
/// </para>
/// <para>
/// On activation, the grain loads its current pricing from the DB (via
/// <see cref="IPricingStore"/>) and subscribes to the <c>pricing</c> stream.
/// On receiving a <see cref="PricingUpdatedStreamEvent"/> that includes this
/// grain's model, it reloads from the DB.
/// </para>
/// </summary>
[StatelessWorker]
public sealed class PricingGrain : Grain, IPricingGrain, IAsyncObserver<PricingUpdatedStreamEvent>
{
    private readonly IPricingStore _store;
    private PricingResult? _cached;
    private StreamSubscriptionHandle<PricingUpdatedStreamEvent>? _subscription;

    /// <summary>
    /// Creates the grain with the given pricing store.
    /// </summary>
    public PricingGrain(IPricingStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Returns the current pricing for this model, or null when unknown.
    /// Read-only on the hot path; the value is cached in-memory and refreshed
    /// via stream events.
    /// </summary>
    public Task<PricingResult?> GetPricingAsync()
    {
        return Task.FromResult(_cached);
    }

    /// <summary>
    /// On activation: loads pricing from the DB and subscribes to the
    /// pricing-updated stream.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var modelName = this.GetPrimaryKeyString();
        await LoadFromStoreAsync(modelName, cancellationToken);

        var streamProvider = this.GetStreamProvider("pricing");
        var stream = streamProvider.GetStream<PricingUpdatedStreamEvent>("pricing", "updates");
        _subscription = await stream.SubscribeAsync(this);
    }

    /// <summary>
    /// On deactivation: unsubscribe from the stream.
    /// </summary>
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            await _subscription.UnsubscribeAsync();
            _subscription = null;
        }
    }

    /// <summary>
    /// Handles a pricing-updated stream event. If the event includes this
    /// grain's model, reloads pricing from the DB.
    /// </summary>
    public async Task OnNextAsync(PricingUpdatedStreamEvent item, StreamSequenceToken? sequenceToken = null)
    {
        var modelName = this.GetPrimaryKeyString();

        if (item.UpdatedModels.Contains(modelName, StringComparer.Ordinal))
        {
            await LoadFromStoreAsync(modelName, CancellationToken.None);
        }
    }

    /// <summary>Not used; required by interface.</summary>
    public Task OnCompletedAsync() => Task.CompletedTask;

    /// <summary>Not used; required by interface.</summary>
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    private async Task LoadFromStoreAsync(string modelName, CancellationToken ct)
    {
        var pricing = await _store.GetByModelAsync(modelName, ct);

        if (pricing is null)
        {
            _cached = null;
            return;
        }

        _cached = new PricingResult
        {
            Input = pricing.Prices.Input,
            Output = pricing.Prices.Output,
            CacheRead = pricing.Prices.CacheRead,
            CacheWrite = pricing.Prices.CacheWrite,
            Currency = pricing.Currency,
            IsStale = pricing.IsStale,
        };
    }
}
