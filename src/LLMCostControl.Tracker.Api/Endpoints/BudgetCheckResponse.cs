using System.Collections.Generic;

namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Response body for <c>POST /api/budget/check</c> (§6.2.1).
/// </summary>
public sealed class BudgetCheckResponse
{
    /// <summary>Whether the caller is allowed to proceed.</summary>
    public bool Allowed { get; init; }

    /// <summary>The caller id.</summary>
    public string CallerId { get; init; } = string.Empty;

    /// <summary>The budgets check details for each configured period.</summary>
    public List<BudgetCheckResponseEntry> Budgets { get; init; } = [];
}
