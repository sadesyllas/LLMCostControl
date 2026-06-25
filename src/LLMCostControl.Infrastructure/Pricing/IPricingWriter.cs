using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Abstracts the pricing write path (§8.4) — replacing all pricing for a
/// provider in one transaction. This is the same persistence operation the
/// refresh job uses; <see cref="PricingImportService"/> depends on this
/// abstraction so the localhost import path (§8.3) reuses the write path
/// without taking a direct dependency on EF Core, and so it can be exercised in
/// tests without a database.
/// </summary>
public interface IPricingWriter
{
    /// <summary>
    /// Replaces all pricing for the given provider with the supplied entries in
    /// a single transaction. Mirrors the refresh job's persist step.
    /// </summary>
    /// <param name="provider">The provider whose pricing is being replaced.</param>
    /// <param name="entries">The full set of model pricing for that provider.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default);
}
