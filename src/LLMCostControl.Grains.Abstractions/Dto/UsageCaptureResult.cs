using System.Collections.Generic;

namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// DTO for the result of a usage capture (§6.2.2).
/// </summary>
[GenerateSerializer]
public sealed record UsageCaptureResult
{
    /// <summary>The caller id.</summary>
    [Id(0)]
    public required string CallerId { get; init; }

    /// <summary>The computed cost amount.</summary>
    [Id(1)]
    public required decimal CostAmount { get; init; }

    /// <summary>The currency of the cost.</summary>
    [Id(2)]
    public required string CostCurrency { get; init; }

    /// <summary>The budgets details for each configured period.</summary>
    [Id(3)]
    public required List<BudgetPeriodCaptureResult> Budgets { get; init; } = [];

    /// <summary>The binding period type (e.g. Monthly or Weekly) that determined the decision.</summary>
    [Id(4)]
    public string? BindingPeriod { get; init; }

    /// <summary>The binding period key (e.g. 2026-06 or 2026-W26).</summary>
    [Id(5)]
    public string? BindingPeriodKey { get; init; }

    /// <summary>The budget source of the binding period.</summary>
    [Id(6)]
    public string? BindingBudgetSource { get; init; }

    /// <summary>The effective group id of the binding period.</summary>
    [Id(7)]
    public Guid? BindingEffectiveGroupId { get; init; }

    /// <summary>Helper property for monthly running spend amount compatibility.</summary>
    public decimal RunningSpendAmount => Budgets.Find(b => b.Period == "Monthly")?.RunningSpendAmount ?? 0m;

    /// <summary>Helper property for monthly remaining amount compatibility.</summary>
    public decimal RemainingAmount => Budgets.Find(b => b.Period == "Monthly")?.RemainingAmount ?? 0m;

    /// <summary>Helper property for monthly budget source compatibility.</summary>
    public Domain.Budgets.BudgetSource BudgetSource => BindingBudgetSource is not null && System.Enum.TryParse<Domain.Budgets.BudgetSource>(BindingBudgetSource, out var src) ? src : Domain.Budgets.BudgetSource.None;

    /// <summary>Helper property for monthly effective group id compatibility.</summary>
    public System.Guid? EffectiveGroupId => BindingEffectiveGroupId;
}
