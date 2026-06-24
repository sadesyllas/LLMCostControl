using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

public record EffectiveBudget
{
    public Money? Amount { get; init; }
    public BudgetSource Source { get; init; }
    public Guid? GroupId { get; init; }

    public bool HasBudget => Amount is not null && !Amount.Value.IsZero;

    public static EffectiveBudget FromUserOverride(Money amount) => new()
    {
        Amount = amount,
        Source = BudgetSource.UserOverride,
        GroupId = null,
    };

    public static EffectiveBudget FromGroup(Money amount, Guid groupId) => new()
    {
        Amount = amount,
        Source = BudgetSource.Group,
        GroupId = groupId,
    };

    public static EffectiveBudget None() => new()
    {
        Amount = null,
        Source = BudgetSource.None,
        GroupId = null,
    };

    public static EffectiveBudget Resolve(
        UserBudgetOverride? userOverride,
        IReadOnlyList<(Guid GroupId, GroupBudget Budget)> groupBudgets)
    {
        if (userOverride is not null)
        {
            return FromUserOverride(userOverride.Amount);
        }

        if (groupBudgets.Count > 0)
        {
            var largest = groupBudgets
                .OrderByDescending(g => g.Budget.Amount.Amount)
                .First();

            return FromGroup(largest.Budget.Amount, largest.GroupId);
        }

        return None();
    }
}
