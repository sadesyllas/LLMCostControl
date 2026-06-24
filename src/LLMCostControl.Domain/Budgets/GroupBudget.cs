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

    /// <summary>The budget period this amount applies to.</summary>
    public BudgetPeriod Period { get; init; }

    /// <summary>When the budget was set.</summary>
    public DateTimeOffset SetAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="GroupBudget"/> for the given group and period.
    /// </summary>
    /// <param name="groupId">The group id; cannot be empty.</param>
    /// <param name="amount">The budget amount; cannot be negative.</param>
    /// <param name="period">The budget period.</param>
    public static GroupBudget Create(Guid groupId, Money amount, BudgetPeriod period)
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
            Period = period,
            SetAt = DateTimeOffset.UtcNow,
        };
    }
}
