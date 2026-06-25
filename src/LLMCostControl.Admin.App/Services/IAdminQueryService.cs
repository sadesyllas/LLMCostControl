using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Read-only queries for the admin app's reporting views (§12.3): effective
/// budget &amp; running spend per caller, current pricing per model/provider with
/// staleness, and recent usage events. All reads go through the shared
/// Infrastructure repositories.
/// </summary>
public interface IAdminQueryService
{
    /// <summary>
    /// Resolves the caller's effective budget and running spend for the current
    /// period, using the same resolution rules as the tracker.
    /// </summary>
    Task<CallerBudgetSummary> GetCallerSummaryAsync(string callerId, CancellationToken ct = default);

    /// <summary>Returns the current pricing for all models (carrying staleness).</summary>
    Task<IReadOnlyList<ModelPricing>> GetPricingAsync(CancellationToken ct = default);

    /// <summary>Returns the caller's recent usage events for the current period (newest first).</summary>
    Task<IReadOnlyList<UsageEvent>> GetRecentUsageAsync(string callerId, int limit = 20, CancellationToken ct = default);
}
