using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Read-only summary of a caller's budget position for the current period (§12.3):
/// the effective budget (resolved by the same rules the tracker uses), the
/// running spend so far, and the remaining budget.
/// </summary>
/// <param name="CallerId">The caller id.</param>
/// <param name="EffectiveBudget">The resolved effective budget (group/override/none).</param>
/// <param name="RunningSpend">The accrued spend for the current period.</param>
/// <param name="Remaining">The remaining budget, or null when the caller is unbudgeted.</param>
public sealed record CallerBudgetSummary(
    string CallerId,
    EffectiveBudget EffectiveBudget,
    Money RunningSpend,
    Money? Remaining);
