namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Abstracts the pricing file import operation (§8.3, §8.5) so that Blazor
/// components and tests can depend on the interface rather than the concrete
/// <see cref="PricingImportService"/>.
/// </summary>
public interface IPricingImportService
{
    /// <summary>
    /// Validates, persists, and publishes the supplied JSON pricing file.
    /// Returns a failure result (with errors) on any validation issue.
    /// </summary>
    Task<PricingImportResult> ImportAsync(string json, CancellationToken ct = default);
}
