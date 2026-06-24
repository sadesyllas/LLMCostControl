using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

public class GroupBudget
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public Money Amount { get; set; }
    public BudgetPeriod Period { get; init; }
    public DateTimeOffset SetAt { get; init; }

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
