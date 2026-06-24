namespace LLMCostControl.Domain.Pricing;

public class ModelPricing
{
    public Guid Id { get; init; }
    public Provider Provider { get; init; }
    public string Model { get; init; } = string.Empty;
    public TokenPrices Prices { get; init; }
    public string Currency { get; init; } = "USD";
    public string Unit { get; init; } = "per-1M-tokens";
    public DateTimeOffset FetchedAt { get; init; }
    public DateTimeOffset? StaleSince { get; set; }

    public bool IsStale => StaleSince is not null;

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
