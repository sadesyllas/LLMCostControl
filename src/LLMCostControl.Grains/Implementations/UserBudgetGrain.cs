using LLMCostControl.Domain.Budgets;
using LLMCostControl.Grains.Abstractions;
using Orleans.Runtime;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Minimal stub implementation of <see cref="IUserBudgetGrain"/> for M8. Returns
/// placeholder results. The real implementation (budget resolution, 30 s TTL,
/// cost accrual, audit trail) is built in M10/M11.
/// </summary>
public sealed class UserBudgetGrain : Grain, IUserBudgetGrain
{
    /// <summary>
    /// Returns a placeholder allowed result for M8 silo bootstrap verification.
    /// </summary>
    public Task<BudgetCheckResult> CheckBudgetAsync()
    {
        var result = new BudgetCheckResult
        {
            Allowed = true,
            CallerId = this.GetPrimaryKeyString(),
            EffectiveBudgetAmount = 100m,
            EffectiveBudgetCurrency = "USD",
            RunningSpendAmount = 0m,
            RunningSpendCurrency = "USD",
            RemainingAmount = 100m,
            RemainingCurrency = "USD",
            BudgetSource = BudgetSource.None,
            EffectiveGroupId = null,
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Returns a placeholder capture result for M8 silo bootstrap verification.
    /// </summary>
    public Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request)
    {
        var result = new UsageCaptureResult
        {
            CallerId = this.GetPrimaryKeyString(),
            CostAmount = 0.01m,
            CostCurrency = "USD",
            RunningSpendAmount = 0.01m,
            RunningSpendCurrency = "USD",
            RemainingAmount = 99.99m,
            RemainingCurrency = "USD",
        };

        return Task.FromResult(result);
    }
}
