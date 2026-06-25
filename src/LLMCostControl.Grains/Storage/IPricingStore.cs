using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Read-side interface for loading model pricing from the database inside a
/// grain. Grains cannot use scoped <c>DbContext</c> directly, so this
/// abstraction wraps a <c>IDbContextFactory</c> behind a transient-safe
/// interface.
/// </summary>
public interface IPricingStore
{
    /// <summary>
    /// Returns the current pricing for the given model name, or null when the
    /// model is unknown / not in the allowed set.
    /// </summary>
    Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default);
}
