using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// An explicit per-user budget that overrides any group budget for the same
/// caller in the same period.
/// </summary>
public class UserBudgetOverride
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The caller this override applies to.</summary>
    public CallerId CallerId { get; init; }

    /// <summary>The budget amount and currency.</summary>
    public required Money Amount { get; set; }

    /// <summary>The budget period type this override applies to.</summary>
    public BudgetPeriodType PeriodType { get; init; }

    /// <summary>When the override was set.</summary>
    public DateTimeOffset SetAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="UserBudgetOverride"/> for the given caller and
    /// period type.
    /// </summary>
    /// <param name="callerId">The caller.</param>
    /// <param name="amount">The budget amount; cannot be negative.</param>
    /// <param name="periodType">The budget period type.</param>
    public static UserBudgetOverride Create(CallerId callerId, Money amount, BudgetPeriodType periodType)
    {
        if (amount.IsNegative)
        {
            throw new ArgumentException("Budget amount cannot be negative.", nameof(amount));
        }

        return new UserBudgetOverride
        {
            Id = Guid.NewGuid(),
            CallerId = callerId,
            Amount = amount,
            PeriodType = periodType,
            SetAt = DateTimeOffset.UtcNow,
        };
    }
}
