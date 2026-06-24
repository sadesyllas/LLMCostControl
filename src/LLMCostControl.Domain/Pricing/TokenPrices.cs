namespace LLMCostControl.Domain.Pricing;

public readonly record struct TokenPrices
{
    public decimal Input { get; init; }
    public decimal Output { get; init; }
    public decimal? CacheRead { get; init; }
    public decimal? CacheWrite { get; init; }

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
