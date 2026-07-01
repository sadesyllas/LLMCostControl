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
