using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.State;
using LLMCostControl.Grains.Storage;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Implementation of <see cref="IUserBudgetGrain"/> keyed by caller id
/// (typically an email). Resolves the effective budget for the caller
/// (per-user override wins; else largest group budget; else none), holds the
/// running spend for the current budget period in persistent state, and
/// answers check and capture calls (§7, §9.1, §12.4, §6.2.2, §9.4).
/// <para>
/// The effective budget is cached with a short TTL
/// (<see cref="BudgetGrainOptions.BudgetCacheTtl"/>, default 30 s) so that
/// admin-side changes become visible without a restart. On each call, the grain
/// first checks whether the current budget period has rolled over; if so, the
/// running spend is reset to zero and the new period is persisted.
/// </para>
/// <para>
/// On capture, the grain looks up <see cref="IPricingGrain"/> for the model,
/// multiplies token counts by unit prices (per 1M tokens), sums to a total
/// cost, accrues it to the running spend, and appends exactly one
/// <see cref="UsageEvent"/> audit row. Duplicate captures (same
/// <c>requestId</c>) are no-ops. Unknown models are rejected with
/// <see cref="UnknownModelException"/>.
/// </para>
/// </summary>
public sealed class UserBudgetGrain : Grain, IUserBudgetGrain
{
    private const decimal TokensPerMillion = 1_000_000m;

    private readonly IBudgetStore _budgetStore;
    private readonly IUsageEventStore _usageEventStore;
    private readonly BudgetGrainOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ProviderInferenceMap _providerInference;

    /// <summary>Persistent grain state, backed by the "Default" Orleans storage provider.</summary>
    private readonly IPersistentState<UserBudgetGrainState> _storage;

    /// <summary>
    /// In-activation cache of the effective budgets.
    /// </summary>
    private EffectiveBudget? _cachedMonthlyBudget;
    private EffectiveBudget? _cachedWeeklyBudget;

    private DateTimeOffset _budgetLoadedAt;

    private string? _cachedMonthlyPeriodKey;
    private string? _cachedWeeklyPeriodKey;

    /// <summary>
    /// Creates the grain with the given budget store, usage event store,
    /// options, time provider, and persistent state storage.
    /// </summary>
    public UserBudgetGrain(
        IBudgetStore budgetStore,
        IUsageEventStore usageEventStore,
        BudgetGrainOptions options,
        TimeProvider timeProvider,
        ProviderInferenceMap providerInference,
        [PersistentState("budget", "Default")] IPersistentState<UserBudgetGrainState> storage)
    {
        _budgetStore = budgetStore;
        _usageEventStore = usageEventStore;
        _options = options;
        _timeProvider = timeProvider;
        _providerInference = providerInference;
        _storage = storage;
    }

    /// <summary>
    /// Checks whether the caller has remaining budget for a new request (§6.2.1).
    /// Resolves the effective budgets (with TTL cache), computes remaining for
    /// each configured period type, and returns Allowed = true when the caller has budget and remaining &gt; 0
    /// across all configured periods.
    /// </summary>
    public async Task<BudgetCheckResult> CheckBudgetAsync()
    {
        await EnsurePeriodsCurrentAsync();
        var (monthly, weekly) = await GetEffectiveBudgetsAsync();
        return BuildCheckResult(monthly, weekly);
    }

    /// <summary>
    /// Captures token usage: looks up pricing via <see cref="IPricingGrain"/>,
    /// computes cost, accrues to running spend for each configured period type,
    /// and appends one <see cref="UsageEvent"/> audit row with child accruals (§6.2.2, §9.4).
    /// </summary>
    public async Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request)
    {
        await EnsurePeriodsCurrentAsync();

        var eventId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString()
            : request.RequestId;

        // Idempotency: if we already captured this requestId, return the
        // original result without re-accruing.
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var existing = await _usageEventStore.GetByIdAsync(eventId);
            if (existing is not null)
            {
                return BuildCaptureResultFromEvent(existing);
            }
        }

        if (!_providerInference.TryResolve(request.Provider, request.Model, out var provider))
        {
            throw new UnknownModelException(request.Model);
        }

        var pricingGrain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(provider, request.Model));
        var pricing = await pricingGrain.GetPricingAsync();

        if (pricing is null)
        {
            throw new UnknownModelException(request.Model);
        }

        var cost = ComputeCost(
            request.TokensInput,
            request.TokensOutput,
            request.TokensCacheRead,
            request.TokensCacheWrite,
            pricing);

        var currency = pricing.Currency;

        var (monthly, weekly) = await GetEffectiveBudgetsAsync();

        // Enforce same currency as budget if budget is present
        if (monthly.HasBudget && !string.Equals(currency, monthly.Amount!.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pricing currency {currency} does not match budget currency {monthly.Amount.Currency}.");
        }
        if (weekly.HasBudget && !string.Equals(currency, weekly.Amount!.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pricing currency {currency} does not match budget currency {weekly.Amount.Currency}.");
        }

        var state = _storage.State;

        // Enforce same currency as existing running spend
        var currentSpend = new Money(state.RunningSpendAmount, state.RunningSpendCurrency);
        var incomingCost = new Money(cost, currency);

        if (!currentSpend.IsZero && !string.Equals(currency, state.RunningSpendCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot combine money in different currencies: Pricing currency {currency} does not match existing running spend currency {state.RunningSpendCurrency}.");
        }

        // Accrue to monthly
        var currentMonthlySpend = new Money(state.MonthlyRunningSpend, state.RunningSpendCurrency);
        state.MonthlyRunningSpend = (currentMonthlySpend.IsZero ? incomingCost : currentMonthlySpend + incomingCost).Amount;

        // Accrue to weekly
        var currentWeeklySpend = new Money(state.WeeklyRunningSpend, state.RunningSpendCurrency);
        state.WeeklyRunningSpend = (currentWeeklySpend.IsZero ? incomingCost : currentWeeklySpend + incomingCost).Amount;

        state.RunningSpendCurrency = currency;
        state.RunningSpendAmount = state.MonthlyRunningSpend;

        try
        {
            await _storage.WriteStateAsync();
        }
        catch (Exception)
        {
            DeactivateOnIdle();
            throw;
        }

        var accruals = new List<UsageEventPeriodAccrual>();

        if (monthly.HasBudget)
        {
            accruals.Add(UsageEventPeriodAccrual.Create(
                eventId: eventId,
                periodType: BudgetPeriodType.Monthly,
                periodKey: state.MonthlyPeriodKey ?? string.Empty,
                effectiveGroupId: monthly.GroupId,
                budgetSource: monthly.Source,
                effectiveBudgetAmount: monthly.Amount!,
                runningSpendAfter: state.MonthlyRunningSpend));
        }

        if (weekly.HasBudget)
        {
            accruals.Add(UsageEventPeriodAccrual.Create(
                eventId: eventId,
                periodType: BudgetPeriodType.Weekly,
                periodKey: state.WeeklyPeriodKey ?? string.Empty,
                effectiveGroupId: weekly.GroupId,
                budgetSource: weekly.Source,
                effectiveBudgetAmount: weekly.Amount!,
                runningSpendAfter: state.WeeklyRunningSpend));
        }

        var usageEvent = UsageEvent.Create(
            eventId: eventId,
            callerId: CallerId.From(this.GetPrimaryKeyString()),
            model: request.Model,
            provider: provider,
            tokensInput: request.TokensInput,
            tokensOutput: request.TokensOutput,
            tokensCacheRead: request.TokensCacheRead,
            tokensCacheWrite: request.TokensCacheWrite,
            pricingVersionId: pricing.PricingVersionId,
            costAmount: cost,
            costCurrency: currency,
            periodAccruals: accruals,
            capturedAt: _timeProvider.GetUtcNow());

        await _usageEventStore.AppendAsync(usageEvent);

        return BuildCaptureResult(monthly, weekly, cost, currency);
    }

    /// <summary>
    /// Computes the total cost from token counts and unit prices (per 1M tokens).
    /// </summary>
    private static decimal ComputeCost(
        long tokensInput,
        long tokensOutput,
        long tokensCacheRead,
        long tokensCacheWrite,
        PricingResult pricing)
    {
        var inputCost = tokensInput * pricing.Input / TokensPerMillion;
        var outputCost = tokensOutput * pricing.Output / TokensPerMillion;
        var cacheReadCost = tokensCacheRead * (pricing.CacheRead ?? 0m) / TokensPerMillion;
        var cacheWriteCost = tokensCacheWrite * (pricing.CacheWrite ?? 0m) / TokensPerMillion;

        return inputCost + outputCost + cacheReadCost + cacheWriteCost;
    }

    /// <summary>
    /// Ensures all grain state periods match the current budget periods.
    /// Resets running spend on rollover.
    /// </summary>
    private async Task EnsurePeriodsCurrentAsync()
    {
        var state = _storage.State;
        var now = _timeProvider.GetUtcNow();
        var currentMonthly = BudgetPeriod.FromDate(now, BudgetPeriodType.Monthly);
        var currentWeekly = BudgetPeriod.FromDate(now, BudgetPeriodType.Weekly);

        bool changed = false;

        // Migrate legacy monthly state
        if (string.IsNullOrEmpty(state.MonthlyPeriodKey) && state.PeriodYear > 0 && state.PeriodMonth > 0)
        {
            state.MonthlyPeriodKey = $"{state.PeriodYear:D4}-{state.PeriodMonth:D2}";
            state.MonthlyRunningSpend = state.RunningSpendAmount;
            changed = true;
        }

        // Check Monthly rollover
        if (state.MonthlyPeriodKey != currentMonthly.Key)
        {
            state.MonthlyPeriodKey = currentMonthly.Key;
            state.MonthlyRunningSpend = 0m;
            changed = true;
        }

        // Check Weekly rollover
        if (state.WeeklyPeriodKey != currentWeekly.Key)
        {
            state.WeeklyPeriodKey = currentWeekly.Key;
            state.WeeklyRunningSpend = 0m;
            changed = true;
        }

        if (changed)
        {
            state.PeriodYear = currentMonthly.Year;
            state.PeriodMonth = currentMonthly.Month;
            state.RunningSpendAmount = state.MonthlyRunningSpend;
            try
            {
                await _storage.WriteStateAsync();
            }
            catch (Exception)
            {
                DeactivateOnIdle();
                throw;
            }
        }
    }

    /// <summary>
    /// Returns the effective budgets for all configured period types.
    /// </summary>
    private async Task<(EffectiveBudget Monthly, EffectiveBudget Weekly)> GetEffectiveBudgetsAsync()
    {
        var now = _timeProvider.GetUtcNow();
        var currentMonthly = BudgetPeriod.FromDate(now, BudgetPeriodType.Monthly);
        var currentWeekly = BudgetPeriod.FromDate(now, BudgetPeriodType.Weekly);

        if (_cachedMonthlyBudget is not null && 
            _cachedWeeklyBudget is not null && 
            _cachedMonthlyPeriodKey == currentMonthly.Key &&
            _cachedWeeklyPeriodKey == currentWeekly.Key &&
            (now - _budgetLoadedAt) < _options.BudgetCacheTtl)
        {
            return (_cachedMonthlyBudget, _cachedWeeklyBudget);
        }

        var callerId = CallerId.From(this.GetPrimaryKeyString());
        
        _cachedMonthlyBudget = await _budgetStore.ResolveAsync(callerId, BudgetPeriodType.Monthly);
        _cachedWeeklyBudget = await _budgetStore.ResolveAsync(callerId, BudgetPeriodType.Weekly);
        
        _cachedMonthlyPeriodKey = currentMonthly.Key;
        _cachedWeeklyPeriodKey = currentWeekly.Key;
        _budgetLoadedAt = now;

        return (_cachedMonthlyBudget, _cachedWeeklyBudget);
    }

    /// <summary>
    /// Builds a <see cref="BudgetCheckResult"/> from effective budgets, resolving the binding period.
    /// </summary>
    private BudgetCheckResult BuildCheckResult(EffectiveBudget monthly, EffectiveBudget weekly)
    {
        var callerId = this.GetPrimaryKeyString();
        var state = _storage.State;
        var budgets = new List<BudgetPeriodCheckResult>();
        bool allowed = true;
        bool hasAnyBudget = false;

        if (monthly.HasBudget)
        {
            hasAnyBudget = true;
            var budgetMoney = monthly.Amount!;
            var runningSpendMoney = new Money(state.MonthlyRunningSpend, state.RunningSpendCurrency);
            if (runningSpendMoney.IsZero)
            {
                runningSpendMoney = Money.Zero(budgetMoney.Currency);
            }
            
            var remainingMoney = budgetMoney - runningSpendMoney;
            var remaining = remainingMoney.Amount;
            if (remaining <= 0m)
            {
                allowed = false;
            }

            budgets.Add(new BudgetPeriodCheckResult
            {
                Period = "Monthly",
                PeriodKey = state.MonthlyPeriodKey ?? string.Empty,
                BudgetSource = monthly.Source,
                EffectiveGroupId = monthly.GroupId,
                EffectiveBudgetAmount = budgetMoney.Amount,
                BudgetCurrency = budgetMoney.Currency,
                RunningSpendAmount = runningSpendMoney.Amount,
                RemainingAmount = remaining
            });
        }

        if (weekly.HasBudget)
        {
            hasAnyBudget = true;
            var budgetMoney = weekly.Amount!;
            var runningSpendMoney = new Money(state.WeeklyRunningSpend, state.RunningSpendCurrency);
            if (runningSpendMoney.IsZero)
            {
                runningSpendMoney = Money.Zero(budgetMoney.Currency);
            }

            var remainingMoney = budgetMoney - runningSpendMoney;
            var remaining = remainingMoney.Amount;
            if (remaining <= 0m)
            {
                allowed = false;
            }

            budgets.Add(new BudgetPeriodCheckResult
            {
                Period = "Weekly",
                PeriodKey = state.WeeklyPeriodKey ?? string.Empty,
                BudgetSource = weekly.Source,
                EffectiveGroupId = weekly.GroupId,
                EffectiveBudgetAmount = budgetMoney.Amount,
                BudgetCurrency = budgetMoney.Currency,
                RunningSpendAmount = runningSpendMoney.Amount,
                RemainingAmount = remaining
            });
        }

        if (!hasAnyBudget)
        {
            allowed = _options.AllowNonBudgetedUsers;
        }

        BudgetPeriodCheckResult? binding = null;
        if (budgets.Count > 0)
        {
            if (!allowed)
            {
                binding = budgets.FirstOrDefault(b => b.RemainingAmount <= 0m) ?? budgets[0];
            }
            else
            {
                binding = budgets.OrderBy(b => b.RemainingAmount).First();
            }
        }

        return new BudgetCheckResult
        {
            Allowed = allowed,
            CallerId = callerId,
            Budgets = budgets,
            BindingPeriod = binding?.Period,
            BindingPeriodKey = binding?.PeriodKey,
            BindingBudgetSource = binding?.BudgetSource.ToString(),
            BindingEffectiveGroupId = binding?.EffectiveGroupId
        };
    }

    /// <summary>
    /// Builds a <see cref="UsageCaptureResult"/> from effective budgets.
    /// </summary>
    private UsageCaptureResult BuildCaptureResult(
        EffectiveBudget monthly,
        EffectiveBudget weekly,
        decimal cost,
        string currency)
    {
        var callerId = this.GetPrimaryKeyString();
        var state = _storage.State;
        var budgets = new List<BudgetPeriodCaptureResult>();

        if (monthly.HasBudget)
        {
            var budgetMoney = monthly.Amount!;
            var runningSpendMoney = new Money(state.MonthlyRunningSpend, currency);
            var remainingMoney = budgetMoney - runningSpendMoney;

            budgets.Add(new BudgetPeriodCaptureResult
            {
                Period = "Monthly",
                PeriodKey = state.MonthlyPeriodKey ?? string.Empty,
                RunningSpendAmount = state.MonthlyRunningSpend,
                RemainingAmount = remainingMoney.Amount
            });
        }

        if (weekly.HasBudget)
        {
            var budgetMoney = weekly.Amount!;
            var runningSpendMoney = new Money(state.WeeklyRunningSpend, currency);
            var remainingMoney = budgetMoney - runningSpendMoney;

            budgets.Add(new BudgetPeriodCaptureResult
            {
                Period = "Weekly",
                PeriodKey = state.WeeklyPeriodKey ?? string.Empty,
                RunningSpendAmount = state.WeeklyRunningSpend,
                RemainingAmount = remainingMoney.Amount
            });
        }

        string? bindingPeriod = null;
        string? bindingPeriodKey = null;
        string? bindingSource = null;
        Guid? bindingGroupId = null;

        if (budgets.Count > 0)
        {
            int bindingIdx = 0;
            decimal minRemaining = budgets[0].RemainingAmount;
            for (int i = 1; i < budgets.Count; i++)
            {
                if (budgets[i].RemainingAmount < minRemaining)
                {
                    minRemaining = budgets[i].RemainingAmount;
                    bindingIdx = i;
                }
            }

            var bindingBudget = budgets[bindingIdx];
            bindingPeriod = bindingBudget.Period;
            bindingPeriodKey = bindingBudget.PeriodKey;
            
            var effBudget = bindingPeriod == "Monthly" ? monthly : weekly;
            bindingSource = effBudget.Source.ToString();
            bindingGroupId = effBudget.GroupId;
        }

        return new UsageCaptureResult
        {
            CallerId = callerId,
            CostAmount = cost,
            CostCurrency = currency,
            Budgets = budgets,
            BindingPeriod = bindingPeriod,
            BindingPeriodKey = bindingPeriodKey,
            BindingBudgetSource = bindingSource,
            BindingEffectiveGroupId = bindingGroupId
        };
    }

    /// <summary>
    /// Builds a <see cref="UsageCaptureResult"/> from an existing UsageEvent.
    /// </summary>
    private UsageCaptureResult BuildCaptureResultFromEvent(UsageEvent evt)
    {
        var budgets = new List<BudgetPeriodCaptureResult>();

        foreach (var acc in evt.PeriodAccruals)
        {
            var remaining = acc.EffectiveBudgetAmount.Amount - acc.RunningSpendAfter;
            budgets.Add(new BudgetPeriodCaptureResult
            {
                Period = acc.PeriodType.ToString(),
                PeriodKey = acc.PeriodKey,
                RunningSpendAmount = acc.RunningSpendAfter,
                RemainingAmount = remaining
            });
        }

        string? bindingPeriod = null;
        string? bindingPeriodKey = null;
        string? bindingSource = null;
        Guid? bindingGroupId = null;

        if (evt.PeriodAccruals.Count > 0)
        {
            var bindingAcc = evt.PeriodAccruals
                .OrderBy(a => a.EffectiveBudgetAmount.Amount - a.RunningSpendAfter)
                .First();

            bindingPeriod = bindingAcc.PeriodType.ToString();
            bindingPeriodKey = bindingAcc.PeriodKey;
            bindingSource = bindingAcc.BudgetSource.ToString();
            bindingGroupId = bindingAcc.EffectiveGroupId;
        }

        return new UsageCaptureResult
        {
            CallerId = evt.CallerId.Value,
            CostAmount = evt.CostAmount,
            CostCurrency = evt.CostCurrency,
            Budgets = budgets,
            BindingPeriod = bindingPeriod,
            BindingPeriodKey = bindingPeriodKey,
            BindingBudgetSource = bindingSource,
            BindingEffectiveGroupId = bindingGroupId
        };
    }
}
