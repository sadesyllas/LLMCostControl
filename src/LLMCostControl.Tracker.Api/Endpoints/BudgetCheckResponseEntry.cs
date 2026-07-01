using System;

namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Detailed budget entry per period in budget check response (§6.2.1).
/// </summary>
public sealed class BudgetCheckResponseEntry
{
    /// <summary>The period type, e.g. "Monthly" or "Weekly".</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>The concrete period key, e.g. "2026-06" or "2026-W26".</summary>
    public string PeriodKey { get; init; } = string.Empty;

    /// <summary>The source of the budget, e.g. Group or UserOverride.</summary>
    public string BudgetSource { get; init; } = string.Empty;

    /// <summary>The effective group id, if the source is a group.</summary>
    public Guid? EffectiveGroupId { get; init; }

    /// <summary>The effective budget limit.</summary>
    public MoneyDto? EffectiveBudget { get; init; }

    /// <summary>The current running spend.</summary>
    public required MoneyDto RunningSpend { get; init; }

    /// <summary>The remaining budget.</summary>
    public required MoneyDto Remaining { get; init; }
}
