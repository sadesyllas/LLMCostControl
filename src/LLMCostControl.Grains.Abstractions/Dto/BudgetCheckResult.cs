using System.Collections.Generic;

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

    /// <summary>The budgets check details for each configured period.</summary>
    [Id(2)]
    public required List<BudgetPeriodCheckResult> Budgets { get; init; } = [];

    /// <summary>The binding period type (e.g. Monthly or Weekly) that determined the decision.</summary>
    [Id(3)]
    public string? BindingPeriod { get; init; }

    /// <summary>The binding period key (e.g. 2026-06 or 2026-W26).</summary>
    [Id(4)]
    public string? BindingPeriodKey { get; init; }

    /// <summary>The budget source of the binding period.</summary>
    [Id(5)]
    public string? BindingBudgetSource { get; init; }

    /// <summary>The effective group id of the binding period.</summary>
    [Id(6)]
    public Guid? BindingEffectiveGroupId { get; init; }

    /// <summary>Helper property for monthly budget amount compatibility.</summary>
    public decimal? EffectiveBudgetAmount => Budgets.Find(b => b.Period == "Monthly")?.EffectiveBudgetAmount;

    /// <summary>Helper property for monthly running spend amount compatibility.</summary>
    public decimal RunningSpendAmount => Budgets.Find(b => b.Period == "Monthly")?.RunningSpendAmount ?? 0m;

    /// <summary>Helper property for monthly remaining amount compatibility.</summary>
    public decimal RemainingAmount => Budgets.Find(b => b.Period == "Monthly")?.RemainingAmount ?? 0m;

    /// <summary>Helper property for monthly remaining currency compatibility.</summary>
    public string RemainingCurrency => Budgets.Find(b => b.Period == "Monthly")?.BudgetCurrency ?? "USD";

    /// <summary>Helper property for monthly budget source compatibility.</summary>
    public Domain.Budgets.BudgetSource BudgetSource => Budgets.Find(b => b.Period == "Monthly")?.BudgetSource ?? Domain.Budgets.BudgetSource.None;

    /// <summary>Helper property for monthly effective group id compatibility.</summary>
    public System.Guid? EffectiveGroupId => Budgets.Find(b => b.Period == "Monthly")?.EffectiveGroupId;
}
