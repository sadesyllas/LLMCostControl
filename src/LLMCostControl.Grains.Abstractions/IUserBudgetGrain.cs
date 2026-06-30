namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// Grain interface keyed by caller id (typically an email). Tracks the caller's
/// running spend for the current budget period, resolves the effective budget,
/// and answers check/capture calls (§9.1, §7).
/// </summary>
public interface IUserBudgetGrain : IGrainWithStringKey
{
    /// <summary>
    /// Checks whether the caller has remaining budget for a new request
    /// (§6.2.1). Returns the effective budget, running spend, and remaining
    /// amount.
    /// </summary>
    Task<BudgetCheckResult> CheckBudgetAsync();

    /// <summary>
    /// Captures token usage, computes cost via <c>PricingGrain</c>, accrues it
    /// to the caller's running spend, and appends an audit row (§6.2.2, §9.4).
    /// Idempotent via RequestId inside the request when supplied.
    /// </summary>
    Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request);
}
