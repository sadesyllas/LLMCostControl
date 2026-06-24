namespace LLMCostControl.Domain.Pricing;

/// <summary>
/// The per-1M-token unit prices for a model: input, output, cache-read, and
/// optional cache-write. Prices are non-negative; cache prices may be null when
/// the model has no cache concept.
/// </summary>
public readonly record struct TokenPrices
{
    /// <summary>Unit price for non-cached input tokens (per 1M tokens).</summary>
    public decimal Input { get; init; }

    /// <summary>Unit price for generated output tokens (per 1M tokens).</summary>
    public decimal Output { get; init; }

    /// <summary>Unit price for cached input tokens; null when no cache concept.</summary>
    public decimal? CacheRead { get; init; }

    /// <summary>Unit price for tokens written to the provider cache; null when N/A.</summary>
    public decimal? CacheWrite { get; init; }

    /// <summary>
    /// Creates a <see cref="TokenPrices"/> value, validating that all supplied
    /// prices are non-negative.
    /// </summary>
    public static TokenPrices Create(
        decimal input,
        decimal output,
        decimal? cacheRead = null,
        decimal? cacheWrite = null)
    {
        if (input < 0m)
        {
            throw new ArgumentException("Input price cannot be negative.", nameof(input));
        }

        if (output < 0m)
        {
            throw new ArgumentException("Output price cannot be negative.", nameof(output));
        }

        if (cacheRead is < 0m)
        {
            throw new ArgumentException("Cache-read price cannot be negative.", nameof(cacheRead));
        }

        if (cacheWrite is < 0m)
        {
            throw new ArgumentException("Cache-write price cannot be negative.", nameof(cacheWrite));
        }

        return new TokenPrices
        {
            Input = input,
            Output = output,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
        };
    }
}
