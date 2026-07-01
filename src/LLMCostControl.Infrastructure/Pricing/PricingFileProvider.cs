using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// A provider section within the pricing file, containing all model entries
/// for that provider.
/// </summary>
public sealed class PricingFileProvider
{
    /// <summary>The provider name: "openai", "anthropic", or "google" (extensible).</summary>
    [JsonRequired]
    public string Provider { get; init; } = string.Empty;

    /// <summary>The model pricing entries for this provider.</summary>
    [JsonRequired]
    public List<PricingFileModel> Models { get; init; } = [];
}
