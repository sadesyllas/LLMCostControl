using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// The budget amount assigned to a group for a specific budget period.
/// </summary>
public class GroupBudget
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The group this budget applies to.</summary>
    public Guid GroupId { get; init; }

    /// <summary>The budget amount and currency.</summary>
    public required Money Amount { get; set; }

    /// <summary>The budget period type this amount applies to.</summary>
    public BudgetPeriodType PeriodType { get; init; }

    /// <summary>When the budget was set.</summary>
    public DateTimeOffset SetAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="GroupBudget"/> for the given group and period type.
    /// </summary>
    /// <param name="groupId">The group id; cannot be empty.</param>
    /// <param name="amount">The budget amount; cannot be negative.</param>
    /// <param name="periodType">The budget period type.</param>
    public static GroupBudget Create(Guid groupId, Money amount, BudgetPeriodType periodType)
    {
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("Group id cannot be empty.", nameof(groupId));
        }

        if (amount.IsNegative)
        {
            throw new ArgumentException("Budget amount cannot be negative.", nameof(amount));
        }

        return new GroupBudget
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Amount = amount,
            PeriodType = periodType,
            SetAt = DateTimeOffset.UtcNow,
        };
    }
}
