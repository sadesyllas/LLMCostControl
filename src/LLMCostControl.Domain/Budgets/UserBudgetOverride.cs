using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

public class UserBudgetOverride
{
    public Guid Id { get; init; }
    public CallerId CallerId { get; init; }
    public Money Amount { get; set; }
    public BudgetPeriod Period { get; init; }
    public DateTimeOffset SetAt { get; init; }

    public static UserBudgetOverride Create(CallerId callerId, Money amount, BudgetPeriod period)
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
            Period = period,
            SetAt = DateTimeOffset.UtcNow,
        };
    }
}
