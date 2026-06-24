namespace LLMCostControl.Domain.Pricing;

/// <summary>
/// The current pricing for a single model from a provider, including the unit
/// prices, currency, unit, fetch timestamp, and an optional staleness marker.
/// </summary>
public class ModelPricing
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The provider that offers this model.</summary>
    public Provider Provider { get; init; }

    /// <summary>The model name as the gateway reports it (join key for capture).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>The unit token prices for this model.</summary>
    public required TokenPrices Prices { get; init; }

    /// <summary>The currency the prices are denominated in (e.g. USD).</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>The pricing unit (e.g. "per-1M-tokens").</summary>
    public string Unit { get; init; } = "per-1M-tokens";

    /// <summary>When the prices were fetched from the source.</summary>
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>
    /// When the prices became stale (fetch failure / fallback). Null when fresh.
    /// </summary>
    public DateTimeOffset? StaleSince { get; set; }

    /// <summary>True when <see cref="StaleSince"/> is set.</summary>
    public bool IsStale => StaleSince is not null;

    /// <summary>
    /// Creates a new <see cref="ModelPricing"/> entry with a fresh id.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="model">The model name; cannot be empty.</param>
    /// <param name="prices">The unit token prices.</param>
    /// <param name="currency">The currency; defaults to USD.</param>
    /// <param name="unit">The pricing unit; defaults to per-1M-tokens.</param>
    /// <param name="fetchedAt">The fetch timestamp; defaults to now.</param>
    public static ModelPricing Create(
        Provider provider,
        string model,
        TokenPrices prices,
        string currency = "USD",
        string unit = "per-1M-tokens",
        DateTimeOffset? fetchedAt = null)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model name cannot be empty.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency cannot be empty.", nameof(currency));
        }

        return new ModelPricing
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Model = model.Trim(),
            Prices = prices,
            Currency = currency,
            Unit = unit,
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
