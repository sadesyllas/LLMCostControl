using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Storage;

namespace LLMCostControl.Grains.Tests;

/// <summary>
/// Stub <see cref="IPricingStore"/> that returns preset pricing values and
/// records read calls. Keyed by the composite <c>"{provider}:{model}"</c> key.
/// Supports updating the preset to simulate a DB change after a stream event.
/// </summary>
public sealed class StubPricingStore : IPricingStore
{
    private readonly Dictionary<string, ModelPricing> _pricing = new(StringComparer.Ordinal);
    private int _callCount;

    /// <summary>Number of times <see cref="GetAsync"/> was called.</summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Sets the pricing to return; the (provider, model) key is derived from the
    /// pricing entry itself.
    /// </summary>
    public void SetPricing(ModelPricing pricing)
        => _pricing[ProviderResolver.Key(pricing.Provider, pricing.Model)] = pricing;

    /// <summary>Removes the pricing for a (provider, model) (simulates unknown model).</summary>
    public void RemovePricing(Provider provider, string model)
        => _pricing.Remove(ProviderResolver.Key(provider, model));

    /// <summary>Resets the store to empty.</summary>
    public void Reset()
    {
        _pricing.Clear();
        _callCount = 0;
    }

    /// <summary>Returns the preset pricing for the (provider, model), or null.</summary>
    public Task<ModelPricing?> GetAsync(Provider provider, string model, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        _pricing.TryGetValue(ProviderResolver.Key(provider, model), out var pricing);
        return Task.FromResult(pricing);
    }
}
