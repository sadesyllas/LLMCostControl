using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Storage;

namespace LLMostControl.Grains.Tests;

/// <summary>
/// Stub <see cref="IPricingStore"/> that returns preset pricing values and
/// records read calls. Supports updating the preset to simulate a DB change
/// after a stream event.
/// </summary>
public sealed class StubPricingStore : IPricingStore
{
    private readonly Dictionary<string, ModelPricing> _pricing = new(StringComparer.Ordinal);
    private int _callCount;

    /// <summary>Number of times <see cref="GetByModelAsync"/> was called.</summary>
    public int CallCount => _callCount;

    /// <summary>Sets the pricing that will be returned for a model name.</summary>
    public void SetPricing(string model, ModelPricing pricing) => _pricing[model] = pricing;

    /// <summary>Removes the pricing for a model (simulates unknown model).</summary>
    public void RemovePricing(string model) => _pricing.Remove(model);

    /// <summary>Returns the preset pricing for the model, or null.</summary>
    public Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        _pricing.TryGetValue(model, out var pricing);
        return Task.FromResult(pricing);
    }
}
