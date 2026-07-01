namespace LLMCostControl.Domain.Common;

/// <summary>
/// Specifies the type of a budget period, e.g. Monthly or Weekly (§7).
/// </summary>
public enum BudgetPeriodType
{
    /// <summary>A calendar month period (e.g. 2026-06).</summary>
    Monthly = 0,

    /// <summary>A weekly period starting on Monday UTC (e.g. 2026-W26).</summary>
    Weekly = 1
}
