namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// Indicates which budget source was in effect for a check or capture decision.
/// </summary>
public enum BudgetSource
{
    /// <summary>The budget came from a group the caller belongs to.</summary>
    Group,

    /// <summary>The budget came from an explicit per-user override.</summary>
    UserOverride,

    /// <summary>The caller has no budget (no group, no override).</summary>
    None,
}
