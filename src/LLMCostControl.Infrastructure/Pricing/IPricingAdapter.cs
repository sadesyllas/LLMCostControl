using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Implemented by each provider's pricing adapter. An adapter knows how to
/// fetch current pricing for its provider's models, encapsulating the specific
/// source (official API, published page, etc.). The refresh job iterates all
/// registered adapters and persists their results.
/// </summary>
public interface IPricingAdapter
{
    /// <summary>The provider this adapter handles.</summary>
    Provider Provider { get; }

    /// <summary>
    /// Fetches the current pricing for this adapter's provider. On failure (or
    /// when no live source is available), falls back to the most recently
    /// persisted value and emits a staleness signal.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The collection of current model prices for this provider.</returns>
    Task<IReadOnlyCollection<ModelPricing>> FetchAsync(CancellationToken ct = default);
}
