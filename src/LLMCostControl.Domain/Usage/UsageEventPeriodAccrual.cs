using System;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Usage;

/// <summary>
/// Records the budget accrual details for a specific period type (Monthly or Weekly)
/// associated with a usage event (§9.4, M21).
/// </summary>
public class UsageEventPeriodAccrual
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The parent usage event ID.</summary>
    public string EventId { get; init; } = string.Empty;

    /// <summary>The period type (Monthly or Weekly).</summary>
    public BudgetPeriodType PeriodType { get; init; }

    /// <summary>The concrete period key instance (e.g. "2026-06" or "2026-W26").</summary>
    public string PeriodKey { get; init; } = string.Empty;

    /// <summary>The group whose budget was in effect, or null for user override.</summary>
    public Guid? EffectiveGroupId { get; init; }

    /// <summary>The budget source used (Group or UserOverride).</summary>
    public BudgetSource BudgetSource { get; init; }

    /// <summary>The effective budget amount at capture time.</summary>
    public required Money EffectiveBudgetAmount { get; init; }

    /// <summary>The caller's running spend for this period after the capture.</summary>
    public decimal RunningSpendAfter { get; init; }

    /// <summary>
    /// Creates a new <see cref="UsageEventPeriodAccrual"/>.
    /// </summary>
    public static UsageEventPeriodAccrual Create(
        string eventId,
        BudgetPeriodType periodType,
        string periodKey,
        Guid? effectiveGroupId,
        BudgetSource budgetSource,
        Money effectiveBudgetAmount,
        decimal runningSpendAfter)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Event id cannot be empty.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(periodKey))
        {
            throw new ArgumentException("Period key cannot be empty.", nameof(periodKey));
        }

        return new UsageEventPeriodAccrual
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            PeriodType = periodType,
            PeriodKey = periodKey,
            EffectiveGroupId = effectiveGroupId,
            BudgetSource = budgetSource,
            EffectiveBudgetAmount = effectiveBudgetAmount,
            RunningSpendAfter = runningSpendAfter
        };
    }
}
