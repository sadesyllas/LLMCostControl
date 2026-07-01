namespace LLMCostControl.Grains.State;

/// <summary>
/// Persistent state for <see cref="Implementations.UserBudgetGrain"/>, stored
/// via the Orleans "Default" storage provider. Holds the current budget period
/// and the running spend for that period so that silo restarts do not lose
/// accrued spend (§9.1).
/// <para>
/// Uses primitive fields (rather than domain types) for Orleans serialization
/// compatibility.
/// </para>
/// </summary>
[GenerateSerializer]
public sealed class UserBudgetGrainState
{
    /// <summary>The year component of the budget period this spend belongs to.</summary>
    [Id(0)]
    public int PeriodYear { get; set; }

    /// <summary>The month component (1–12) of the budget period.</summary>
    [Id(1)]
    public int PeriodMonth { get; set; }

    /// <summary>The running spend accrued in the current period.</summary>
    [Id(2)]
    public decimal RunningSpendAmount { get; set; }

    /// <summary>The currency of the running spend (3-letter ISO code).</summary>
    [Id(3)]
    public string RunningSpendCurrency { get; set; } = "USD";

    /// <summary>The active key for the monthly period (e.g. 2026-06).</summary>
    [Id(4)]
    public string? MonthlyPeriodKey { get; set; }

    /// <summary>The running spend for the monthly period.</summary>
    [Id(5)]
    public decimal MonthlyRunningSpend { get; set; }

    /// <summary>The active key for the weekly period (e.g. 2026-W26).</summary>
    [Id(6)]
    public string? WeeklyPeriodKey { get; set; }

    /// <summary>The running spend for the weekly period.</summary>
    [Id(7)]
    public decimal WeeklyRunningSpend { get; set; }
}
