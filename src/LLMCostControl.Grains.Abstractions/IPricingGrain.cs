using Orleans;

namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// DTO for the current pricing of a model, returned by <see cref="IPricingGrain"/>.
/// </summary>
[GenerateSerializer]
public sealed record PricingResult
{
    /// <summary>Unit price for non-cached input tokens (per 1M tokens).</summary>
    [Id(0)]
    public required decimal Input { get; init; }

    /// <summary>Unit price for generated output tokens (per 1M tokens).</summary>
    [Id(1)]
    public required decimal Output { get; init; }

    /// <summary>Unit price for cached input tokens; null when no cache concept.</summary>
    [Id(2)]
    public decimal? CacheRead { get; init; }

    /// <summary>Unit price for tokens written to cache; null when N/A.</summary>
    [Id(3)]
    public decimal? CacheWrite { get; init; }

    /// <summary>The currency the prices are denominated in.</summary>
    [Id(4)]
    public required string Currency { get; init; }

    /// <summary>True when the pricing is stale (fallback was used).</summary>
    [Id(5)]
    public bool IsStale { get; init; }
}

/// <summary>
/// Grain interface keyed by model name. Holds the model's current unit prices
/// in-silo so the capture path can compute cost without a DB round-trip (§8.6).
/// Implemented as a <c>[StatelessWorker]</c> local grain.
/// </summary>
public interface IPricingGrain : IGrainWithStringKey
{
    /// <summary>
    /// Returns the current pricing for this model, or null when the model is
    /// unknown / not in the allowed set.
    /// </summary>
    Task<PricingResult?> GetPricingAsync();
}
