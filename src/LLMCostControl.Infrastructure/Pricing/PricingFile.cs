using System.Text.Json.Serialization;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Root DTO for the canonical pricing file (§8.5). Binds the JSON structure:
/// <code>
/// { "generatedAt": "...", "currency": "USD", "unit": "per-1M-tokens",
///   "providers": [ { "provider": "openai", "models": [...] }, ... ] }
/// </code>
/// </summary>
public sealed class PricingFile
{
    /// <summary>When the file was generated (ISO-8601).</summary>
    [JsonRequired]
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>The currency all prices are denominated in (3-letter ISO code).</summary>
    [JsonRequired]
    public string Currency { get; init; } = string.Empty;

    /// <summary>The pricing unit (e.g. "per-1M-tokens").</summary>
    [JsonRequired]
    public string Unit { get; init; } = string.Empty;

    /// <summary>The providers and their model pricing entries.</summary>
    [JsonRequired]
    public List<PricingFileProvider> Providers { get; init; } = [];
}

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
