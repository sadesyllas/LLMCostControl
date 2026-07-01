namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// A money amount with currency, used in API responses.
/// </summary>
public sealed class MoneyDto
{
    /// <summary>The numeric amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>The 3-letter ISO currency code.</summary>
    public string Currency { get; init; } = "USD";
}
