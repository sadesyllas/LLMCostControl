namespace LLMCostControl.Tracker.Api.Endpoints;

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
