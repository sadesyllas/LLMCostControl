namespace LLMCostControl.Tracker.Api.Endpoints;

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
