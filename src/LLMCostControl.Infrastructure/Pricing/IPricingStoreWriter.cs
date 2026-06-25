using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Abstracts the write path for model pricing data. Used by
/// <see cref="PricingImportService"/> to persist imported pricing entries
/// without coupling directly to <see cref="Repositories.ModelPricingRepository"/>,
/// enabling testable stub implementations.
/// </summary>
public interface IPricingStoreWriter
{
    /// <summary>
    /// Replaces all pricing entries for the given provider atomically.
    /// </summary>
    /// <param name="provider">The provider whose pricing is being replaced.</param>
    /// <param name="entries">The replacement pricing entries (non-empty).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReplaceProviderAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default);
}
