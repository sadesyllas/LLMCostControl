using System;
using LLMCostControl.Domain.Budgets;

namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// Budget check result details for a specific budget period dimension (§6.2.1).
/// </summary>
[GenerateSerializer]
public sealed record BudgetPeriodCheckResult
{
    /// <summary>The period type, e.g. "Monthly" or "Weekly".</summary>
    [Id(0)]
    public required string Period { get; init; }

    /// <summary>The concrete period key instance, e.g. "2026-06" or "2026-W26".</summary>
    [Id(1)]
    public required string PeriodKey { get; init; }

    /// <summary>The budget source used (Group or UserOverride).</summary>
    [Id(2)]
    public required BudgetSource BudgetSource { get; init; }

    /// <summary>The effective group id, if the source is a group.</summary>
    [Id(3)]
    public Guid? EffectiveGroupId { get; init; }

    /// <summary>The effective budget amount.</summary>
    [Id(4)]
    public required decimal EffectiveBudgetAmount { get; init; }

    /// <summary>The budget currency.</summary>
    [Id(5)]
    public required string BudgetCurrency { get; init; }

    /// <summary>The caller's running spend amount.</summary>
    [Id(6)]
    public required decimal RunningSpendAmount { get; init; }

    /// <summary>The remaining budget amount.</summary>
    [Id(7)]
    public required decimal RemainingAmount { get; init; }
}
