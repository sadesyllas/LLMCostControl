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


