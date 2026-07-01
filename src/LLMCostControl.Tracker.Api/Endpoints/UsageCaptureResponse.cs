using System.Collections.Generic;

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

    /// <summary>The budgets details for each configured period.</summary>
    public List<UsageCaptureResponseEntry> Budgets { get; init; } = [];
}
