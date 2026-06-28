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
    public static EffectiveBudget FromUserOverride(Money amount) => new()
    {
        Amount = amount,
        Source = BudgetSource.UserOverride,
        GroupId = null,
    };

    /// <summary>Creates an <see cref="EffectiveBudget"/> from a group budget.</summary>
    public static EffectiveBudget FromGroup(Money amount, Guid groupId) => new()
    {
        Amount = amount,
        Source = BudgetSource.Group,
        GroupId = groupId,
    };

    /// <summary>Creates an <see cref="EffectiveBudget"/> representing no budget.</summary>
    public static EffectiveBudget None() => new()
    {
        Amount = null,
        Source = BudgetSource.None,
        GroupId = null,
    };

    /// <summary>
    /// Resolves the effective budget: per-user override wins; otherwise the
    /// largest group budget; otherwise none.
    /// </summary>
    /// <param name="userOverride">The caller's per-user override, if any.</param>
    /// <param name="groupBudgets">The caller's group budgets, keyed by group id.</param>
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
