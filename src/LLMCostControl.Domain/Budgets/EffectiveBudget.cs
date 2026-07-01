using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// The result of resolving the effective budget for a caller. Encapsulates the
/// resolved amount, the source (group / override / none), and the originating
/// group id when applicable.
/// </summary>
public record EffectiveBudget
{
    /// <summary>The resolved budget amount, or null when the caller has no budget.</summary>
    public Money? Amount { get; init; }

    /// <summary>Which source provided the budget.</summary>
    public BudgetSource Source { get; init; }

    /// <summary>The group id whose budget was in effect, or null for override/none.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>The budget period type in effect.</summary>
    public BudgetPeriodType PeriodType { get; init; }

    /// <summary>
    /// True when the caller has an explicit budget — <b>including an explicit zero</b>.
    /// A zero budget is a real, intentional "spend nothing" budget (it denies on
    /// check because remaining is 0) and must be distinguished from having
    /// <em>no</em> budget at all (<see cref="None"/>), which is what the
    /// <c>AllowNonBudgetedUsers</c> setting governs (§7). Setting a group budget
    /// to 0 is therefore a reliable cut-off, regardless of that setting.
    /// </summary>
    public bool HasBudget => Amount is not null;

    /// <summary>Creates an <see cref="EffectiveBudget"/> from a per-user override.</summary>
    public static EffectiveBudget FromUserOverride(Money amount, BudgetPeriodType periodType = BudgetPeriodType.Monthly) => new()
    {
        Amount = amount,
        Source = BudgetSource.UserOverride,
        GroupId = null,
        PeriodType = periodType,
    };

    /// <summary>Creates an <see cref="EffectiveBudget"/> from a group budget.</summary>
    public static EffectiveBudget FromGroup(Money amount, Guid groupId, BudgetPeriodType periodType = BudgetPeriodType.Monthly) => new()
    {
        Amount = amount,
        Source = BudgetSource.Group,
        GroupId = groupId,
        PeriodType = periodType,
    };

    /// <summary>Creates an <see cref="EffectiveBudget"/> representing no budget.</summary>
    public static EffectiveBudget None(BudgetPeriodType periodType = BudgetPeriodType.Monthly) => new()
    {
        Amount = null,
        Source = BudgetSource.None,
        GroupId = null,
        PeriodType = periodType,
    };

    /// <summary>
    /// Resolves the effective budget: per-user override wins; otherwise the
    /// largest group budget; otherwise none.
    /// </summary>
    /// <param name="userOverride">The caller's per-user override, if any.</param>
    /// <param name="groupBudgets">The caller's group budgets, keyed by group id.</param>
    /// <param name="periodType">The budget period type to resolve.</param>
    public static EffectiveBudget Resolve(
        UserBudgetOverride? userOverride,
        IReadOnlyList<(Guid GroupId, GroupBudget Budget)> groupBudgets,
        BudgetPeriodType periodType)
    {
        if (userOverride is not null)
        {
            return FromUserOverride(userOverride.Amount, periodType);
        }

        if (groupBudgets.Count > 0)
        {
            var largest = groupBudgets
                .OrderByDescending(g => g.Budget.Amount.Amount)
                .First();

            return FromGroup(largest.Budget.Amount, largest.GroupId, periodType);
        }

        return None(periodType);
    }
}
