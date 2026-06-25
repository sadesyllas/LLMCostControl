using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// A summary of a caller's effective budget and current running spend for a
/// given period, computed from the shared repositories.
/// </summary>
public sealed class CallerBudgetSummary
{
    /// <summary>The effective budget resolved for this caller.</summary>
    public required EffectiveBudget EffectiveBudget { get; init; }

    /// <summary>Running spend in the period (sum of captured cost rows).</summary>
    public decimal RunningSpend { get; init; }

    /// <summary>The currency of the running spend.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Remaining budget, or 0 when no budget is set.</summary>
    public decimal Remaining =>
        EffectiveBudget.HasBudget
            ? EffectiveBudget.Amount!.Amount - RunningSpend
            : 0m;
}

/// <summary>
/// Read-only views for the admin panel (§12.3): caller budget summary, current
/// pricing, and recent usage events.
/// </summary>
public interface IAdminReadService
{
    /// <summary>
    /// Resolves the effective budget and computes the running spend for a caller
    /// in the given period. Returns null when the callerId is empty or invalid.
    /// </summary>
    Task<CallerBudgetSummary?> GetCallerSummaryAsync(
        string callerId,
        BudgetPeriod period,
        CancellationToken ct = default);

    /// <summary>Returns all current model pricing entries.</summary>
    Task<IReadOnlyList<ModelPricing>> GetAllPricingAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent usage events for a caller in the given period,
    /// ordered by capture time descending.
    /// </summary>
    Task<IReadOnlyList<UsageEvent>> GetRecentEventsAsync(
        string callerId,
        BudgetPeriod period,
        CancellationToken ct = default);
}
