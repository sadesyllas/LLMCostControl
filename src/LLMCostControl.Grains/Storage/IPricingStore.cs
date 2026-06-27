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
    /// Returns the current pricing for the given (provider, model) pair, or null
    /// when it is unknown / not in the allowed set (§8.5).
    /// </summary>
    Task<ModelPricing?> GetAsync(Provider provider, string model, CancellationToken ct = default);
}
