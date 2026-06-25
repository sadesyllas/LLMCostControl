using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.State;
using LLMCostControl.Grains.Storage;
using Orleans.Runtime;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Implementation of <see cref="IUserBudgetGrain"/> keyed by caller id
/// (typically an email). Resolves the effective budget for the caller
/// (per-user override wins; else largest group budget; else none), holds the
/// running spend for the current budget period in persistent state, and
/// answers check calls (§7, §9.1, §12.4).
/// <para>
/// The effective budget is cached with a short TTL
/// (<see cref="BudgetGrainOptions.BudgetCacheTtl"/>, default 30 s) so that
/// admin-side changes become visible without a restart. On each call, the grain
/// first checks whether the current budget period has rolled over; if so, the
/// running spend is reset to zero and the new period is persisted.
/// </para>
/// <para>
/// Capture accrual (cost computation via <c>PricingGrain</c>) is implemented in
/// M11. In M10, <see cref="CaptureUsageAsync"/> resolves the budget and returns
/// current state without accruing cost.
/// </para>
/// </summary>
public sealed class UserBudgetGrain : Grain, IUserBudgetGrain
{
    private readonly IBudgetStore _store;
    private readonly BudgetGrainOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Persistent grain state, backed by the "Default" Orleans storage provider.</summary>
    private readonly IPersistentState<UserBudgetGrainState> _storage;

    /// <summary>
    /// In-activation cache of the effective budget, with the timestamp it was
    /// loaded. Re-read from the DB when the TTL expires.
    /// </summary>
    private EffectiveBudget? _cachedBudget;

    private DateTimeOffset _budgetLoadedAt;

    /// <summary>
    /// Creates the grain with the given budget store, options, time provider,
    /// and persistent state storage.
    /// </summary>
    public UserBudgetGrain(
        IBudgetStore store,
        BudgetGrainOptions options,
        TimeProvider timeProvider,
        [PersistentState("budget", "Default")] IPersistentState<UserBudgetGrainState> storage)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
        _storage = storage;
    }

    /// <summary>
    /// Checks whether the caller has remaining budget for a new request
    /// (§6.2.1). Resolves the effective budget (with TTL cache), computes
    /// remaining = effective budget − running spend, and returns
    /// <c>Allowed = true</c> when the caller has budget and remaining &gt; 0,
    /// or when <see cref="BudgetGrainOptions.AllowNonBudgetedUsers"/> is
    /// <c>true</c> and the caller has no budget. Unbudgeted callers are
    /// denied (fail-closed) by default.
    /// </summary>
    public async Task<BudgetCheckResult> CheckBudgetAsync()
    {
        await EnsurePeriodCurrentAsync();
        var budget = await GetEffectiveBudgetAsync();
        return BuildCheckResult(budget);
    }

    /// <summary>
    /// M10 implementation: resolves the effective budget and returns the
    /// current running spend and remaining budget without accruing cost.
    /// Cost computation and accrual are added in M11.
    /// </summary>
    public async Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request)
    {
        await EnsurePeriodCurrentAsync();
        var budget = await GetEffectiveBudgetAsync();
        return BuildCaptureResult(budget, costAmount: 0m);
    }

    /// <summary>
    /// Ensures the grain state's period matches the current budget period.
    /// On rollover (or first activation), resets the running spend to zero
    /// and persists the new period.
    /// </summary>
    private async Task EnsurePeriodCurrentAsync()
    {
        var state = _storage.State;
        var current = CurrentPeriod();

        if (state.PeriodYear != current.Year || state.PeriodMonth != current.Month)
        {
            state.PeriodYear = current.Year;
            state.PeriodMonth = current.Month;
            state.RunningSpendAmount = 0m;
            await _storage.WriteStateAsync();
        }
    }

    /// <summary>
    /// Returns the effective budget, using the in-activation TTL cache when
    /// fresh, otherwise re-reading from the store.
    /// </summary>
    private async Task<EffectiveBudget> GetEffectiveBudgetAsync()
    {
        var now = _timeProvider.GetUtcNow();

        if (_cachedBudget is not null && (now - _budgetLoadedAt) < _options.BudgetCacheTtl)
        {
            return _cachedBudget;
        }

        var callerId = CallerId.From(this.GetPrimaryKeyString());
        _cachedBudget = await _store.ResolveAsync(callerId, CurrentPeriod());
        _budgetLoadedAt = now;
        return _cachedBudget;
    }

    /// <summary>The current budget period derived from the time provider.</summary>
    private BudgetPeriod CurrentPeriod() =>
        BudgetPeriod.FromDate(_timeProvider.GetUtcNow());

    /// <summary>
    /// Builds a <see cref="BudgetCheckResult"/> from the effective budget and
    /// current running spend.
    /// </summary>
    private BudgetCheckResult BuildCheckResult(EffectiveBudget budget)
    {
        var callerId = this.GetPrimaryKeyString();
        var runningSpend = _storage.State.RunningSpendAmount;
        var runningCurrency = _storage.State.RunningSpendCurrency;

        if (budget.HasBudget)
        {
            var budgetAmount = budget.Amount!.Amount;
            var budgetCurrency = budget.Amount.Currency;
            var remaining = budgetAmount - runningSpend;

            return new BudgetCheckResult
            {
                Allowed = remaining > 0m,
                CallerId = callerId,
                EffectiveBudgetAmount = budgetAmount,
                EffectiveBudgetCurrency = budgetCurrency,
                RunningSpendAmount = runningSpend,
                RunningSpendCurrency = runningCurrency,
                RemainingAmount = remaining,
                RemainingCurrency = budgetCurrency,
                BudgetSource = budget.Source,
                EffectiveGroupId = budget.GroupId,
            };
        }

        return new BudgetCheckResult
        {
            Allowed = _options.AllowNonBudgetedUsers,
            CallerId = callerId,
            EffectiveBudgetAmount = null,
            EffectiveBudgetCurrency = null,
            RunningSpendAmount = runningSpend,
            RunningSpendCurrency = runningCurrency,
            RemainingAmount = 0m,
            RemainingCurrency = runningCurrency,
            BudgetSource = BudgetSource.None,
            EffectiveGroupId = null,
        };
    }

    /// <summary>
    /// Builds a <see cref="UsageCaptureResult"/> from the effective budget,
    /// current running spend, and the given cost amount (zero in M10).
    /// </summary>
    private UsageCaptureResult BuildCaptureResult(EffectiveBudget budget, decimal costAmount)
    {
        var callerId = this.GetPrimaryKeyString();
        var runningSpend = _storage.State.RunningSpendAmount;
        var runningCurrency = _storage.State.RunningSpendCurrency;

        decimal remainingAmount;
        string remainingCurrency;

        if (budget.HasBudget)
        {
            remainingAmount = budget.Amount!.Amount - runningSpend;
            remainingCurrency = budget.Amount.Currency;
        }
        else
        {
            remainingAmount = 0m;
            remainingCurrency = runningCurrency;
        }

        return new UsageCaptureResult
        {
            CallerId = callerId,
            CostAmount = costAmount,
            CostCurrency = runningCurrency,
            RunningSpendAmount = runningSpend,
            RunningSpendCurrency = runningCurrency,
            RemainingAmount = remainingAmount,
            RemainingCurrency = remainingCurrency,
        };
    }
}
