namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// A test-only grain interface seam to support deactivation testing for <see cref="IUserBudgetGrain"/>.
/// </summary>
public interface ITestUserBudgetGrain : IGrainWithStringKey
{
    /// <summary>
    /// Forcefully deactivates the grain activation on idle.
    /// Used primarily for testing reactivation, ledger reconstruction, and cross-activation idempotency.
    /// </summary>
    Task DeactivateOnIdleAsync();
}
