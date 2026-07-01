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

    /// <summary>The effective budget, or null when no budget.</summary>
    public MoneyDto? EffectiveBudget { get; init; }

    /// <summary>The caller's current running spend.</summary>
    public required MoneyDto RunningSpend { get; init; }

    /// <summary>The remaining budget.</summary>
    public required MoneyDto Remaining { get; init; }
}
