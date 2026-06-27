namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Request body for <c>POST /api/budget/check</c> (§6.2.1).
/// </summary>
public sealed class BudgetCheckRequest
{
    /// <summary>The budget subject (typically an email).</summary>
    public string CallerId { get; set; } = string.Empty;

    /// <summary>Optional model name for early model gating.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Optional provider (<c>openai</c> | <c>anthropic</c> | <c>google</c>);
    /// inferred from the model name when omitted (§6.2.3).
    /// </summary>
    public string? Provider { get; set; }
}

/// <summary>
/// Response body for <c>POST /api/budget/check</c> (§6.2.1).
/// </summary>
public sealed class BudgetCheckResponse
{
    /// <summary>Whether the caller is allowed to proceed.</summary>
    public bool Allowed { get; init; }

    /// <summary>The caller id.</summary>
    public string CallerId { get; init; } = string.Empty;

    /// <summary>The effective budget, or null when no budget.</summary>
    public MoneyDto? EffectiveBudget { get; init; }

    /// <summary>The caller's current running spend.</summary>
    public required MoneyDto RunningSpend { get; init; }

    /// <summary>The remaining budget.</summary>
    public required MoneyDto Remaining { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/usage/capture</c> (§6.2.2).
/// </summary>
public sealed class UsageCaptureDto
{
    /// <summary>The caller whose spend was accrued.</summary>
    public string CallerId { get; set; } = string.Empty;

    /// <summary>The model name reported by the gateway.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Optional provider (<c>openai</c> | <c>anthropic</c> | <c>google</c>);
    /// inferred from the model name when omitted (§6.2.3).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Token counts.</summary>
    public required TokenCountsDto Tokens { get; set; }

    /// <summary>Optional idempotency key (the gateway's requestId).</summary>
    public string? RequestId { get; set; }
}

/// <summary>
/// Token counts sub-object for <see cref="UsageCaptureDto"/>.
/// </summary>
public sealed class TokenCountsDto
{
    /// <summary>Non-cached input token count.</summary>
    public long Input { get; set; }

    /// <summary>Generated output token count.</summary>
    public long Output { get; set; }

    /// <summary>Cached input token count (provider cache hit).</summary>
    public long CacheRead { get; set; }

    /// <summary>Tokens written to the provider cache.</summary>
    public long CacheWrite { get; set; }
}

/// <summary>
/// Response body for <c>POST /api/usage/capture</c> (§6.2.2).
/// </summary>
public sealed class UsageCaptureResponse
{
    /// <summary>The caller id.</summary>
    public string CallerId { get; init; } = string.Empty;

    /// <summary>The computed cost.</summary>
    public required MoneyDto Cost { get; init; }

    /// <summary>The caller's running spend after this capture.</summary>
    public required MoneyDto RunningSpend { get; init; }

    /// <summary>The remaining budget after this capture.</summary>
    public required MoneyDto Remaining { get; init; }
}

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

/// <summary>
/// Error response body for API errors.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>The error type.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Human-readable detail.</summary>
    public string? Detail { get; init; }
}
