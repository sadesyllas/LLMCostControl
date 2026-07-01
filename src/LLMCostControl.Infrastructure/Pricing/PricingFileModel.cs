using System;
using System.Text.Json.Serialization;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// A single model's pricing entry within the pricing file.
/// </summary>
public sealed class PricingFileModel
{
    /// <summary>The model name as the gateway reports it (join key for capture).</summary>
    [JsonRequired]
    public string Model { get; init; } = string.Empty;

    /// <summary>When these prices were obtained (ISO-8601).</summary>
    [JsonRequired]
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>The unit token prices for this model.</summary>
    [JsonRequired]
    public PricingFilePrices Prices { get; init; } = new();
}
