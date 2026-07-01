using System.Text.Json.Serialization;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// The price components for a model entry in the pricing file.
/// </summary>
public sealed class PricingFilePrices
{
    /// <summary>Unit price for non-cached input tokens (mandatory, non-null).</summary>
    [JsonRequired]
    public decimal Input { get; init; }

    /// <summary>Unit price for generated output tokens (mandatory, non-null).</summary>
    [JsonRequired]
    public decimal Output { get; init; }

    /// <summary>
    /// Unit price for cached input tokens; null when the model has no cache
    /// concept (field is mandatory, value may be null).
    /// </summary>
    [JsonRequired]
    public decimal? CacheRead { get; init; }

    /// <summary>
    /// Unit price for tokens written to the provider cache; null when not
    /// applicable (optional).
    /// </summary>
    public decimal? CacheWrite { get; init; }
}
