namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// Budget capture result details for a specific budget period dimension (§6.2.2).
/// </summary>
[GenerateSerializer]
public sealed record BudgetPeriodCaptureResult
{
    /// <summary>The period type, e.g. "Monthly" or "Weekly".</summary>
    [Id(0)]
    public required string Period { get; init; }

    /// <summary>The concrete period key instance, e.g. "2026-06" or "2026-W26".</summary>
    [Id(1)]
    public required string PeriodKey { get; init; }

    /// <summary>The caller's running spend amount.</summary>
    [Id(2)]
    public required decimal RunningSpendAmount { get; init; }

    /// <summary>The remaining budget amount.</summary>
    [Id(3)]
    public required decimal RemainingAmount { get; init; }
}
