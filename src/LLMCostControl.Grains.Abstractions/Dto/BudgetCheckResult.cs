using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Orleans;

namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// DTO for the result of a budget check (§6.2.1).
/// </summary>
[GenerateSerializer]
public sealed record BudgetCheckResult
{
    /// <summary>Whether the caller is allowed to proceed.</summary>
    [Id(0)]
    public required bool Allowed { get; init; }

    /// <summary>The caller id.</summary>
    [Id(1)]
    public required string CallerId { get; init; }

    /// <summary>The effective budget amount, or null when no budget.</summary>
    [Id(2)]
    public decimal? EffectiveBudgetAmount { get; init; }

    /// <summary>The currency of the effective budget.</summary>
    [Id(3)]
    public string? EffectiveBudgetCurrency { get; init; }

    /// <summary>The caller's current running spend amount.</summary>
    [Id(4)]
    public required decimal RunningSpendAmount { get; init; }

    /// <summary>The currency of the running spend.</summary>
    [Id(5)]
    public required string RunningSpendCurrency { get; init; }

    /// <summary>The remaining budget amount.</summary>
    [Id(6)]
    public required decimal RemainingAmount { get; init; }

    /// <summary>The currency of the remaining budget.</summary>
    [Id(7)]
    public required string RemainingCurrency { get; init; }

    /// <summary>The source of the effective budget.</summary>
    [Id(8)]
    public BudgetSource BudgetSource { get; init; }

    /// <summary>The effective group id, if the source is a group.</summary>
    [Id(9)]
    public Guid? EffectiveGroupId { get; init; }
}
