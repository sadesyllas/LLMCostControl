using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
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
    /// In-activation cache of the effective budget, with the timestamp it was
    /// loaded. Re-read from the DB when the TTL expires.
    /// </summary>
    private EffectiveBudget? _cachedBudget;

    private DateTimeOffset _budgetLoadedAt;

    private BudgetPeriod? _cachedBudgetPeriod;

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
    /// Captures token usage: looks up pricing via <see cref="IPricingGrain"/>,
    /// computes cost, accrues to running spend, and appends one
    /// <see cref="UsageEvent"/> audit row (§6.2.2, §9.4). Idempotent via
    /// <paramref name="request"/>.<see cref="UsageCaptureRequest.RequestId"/>
    /// — a duplicate capture returns the original result without
    /// re-accruing. Throws <see cref="UnknownModelException"/> when the model
    /// is not in the pricing set.
    /// </summary>
    public async Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request)
    {
        await EnsurePeriodCurrentAsync();

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

        // Resolve the provider (explicit on the request, else inferred from the
        // model name via the config-driven map, §6.2.3) and look up pricing for
        // the (provider, model) pair via its composite-keyed PricingGrain (§8.6).
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

        // Compute cost (prices are per 1M tokens).
        var cost = ComputeCost(
            request.TokensInput,
            request.TokensOutput,
            request.TokensCacheRead,
            request.TokensCacheWrite,
            pricing);

        var currency = pricing.Currency;

        // Resolve effective budget.
        var budget = await GetEffectiveBudgetAsync();

        // Accrue to running spend.
        var state = _storage.State;

        // Enforce same currency as budget if budget is present
        if (budget.HasBudget && !string.Equals(currency, budget.Amount!.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Pricing currency {currency} does not match budget currency {budget.Amount.Currency}.");
        }

        // Use domain Money to enforce currency validation with existing running spend
        var currentSpend = new Money(state.RunningSpendAmount, state.RunningSpendCurrency);
        var incomingCost = new Money(cost, currency);

        Money newSpend;
        if (currentSpend.IsZero)
        {
            newSpend = incomingCost;
        }
        else
        {
            newSpend = currentSpend + incomingCost; // Throws if currencies mismatch
        }

        try
        {
            state.RunningSpendAmount = newSpend.Amount;
            state.RunningSpendCurrency = newSpend.Currency;
            await _storage.WriteStateAsync();
        }
        catch (Exception)
        {
            DeactivateOnIdle();
            throw;
        }

        // Append the usage event audit row.
        var usageEvent = UsageEvent.Create(
            eventId,
            CallerId.From(this.GetPrimaryKeyString()),
            budget.GroupId,
            budget.Source,
            request.Model,
            provider,
            request.TokensInput,
            request.TokensOutput,
            request.TokensCacheRead,
            request.TokensCacheWrite,
            TokenPrices.Create(pricing.Input, pricing.Output, pricing.CacheRead, pricing.CacheWrite),
            cost,
            currency,
            state.RunningSpendAmount,
            CurrentPeriod(),
            _timeProvider.GetUtcNow());

        await _usageEventStore.AppendAsync(usageEvent);

        return BuildCaptureResult(budget, cost, state.RunningSpendAmount, currency);
    }

    /// <summary>
    /// Computes the total cost from token counts and unit prices (per 1M
    /// tokens). Null cache prices are treated as zero.
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
        var currentPeriod = CurrentPeriod();

        if (_cachedBudget is not null && _cachedBudgetPeriod == currentPeriod && (now - _budgetLoadedAt) < _options.BudgetCacheTtl)
        {
            return _cachedBudget;
        }

        var callerId = CallerId.From(this.GetPrimaryKeyString());
        _cachedBudget = await _budgetStore.ResolveAsync(callerId, currentPeriod);
        _cachedBudgetPeriod = currentPeriod;
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
            var budgetMoney = budget.Amount!;
            var runningSpendMoney = new Money(runningSpend, runningCurrency);

            if (runningSpendMoney.IsZero)
            {
                runningSpendMoney = Money.Zero(budgetMoney.Currency);
            }

            var remainingMoney = budgetMoney - runningSpendMoney; // Throws on mismatch

            return new BudgetCheckResult
            {
                Allowed = remainingMoney.Amount > 0m,
                CallerId = callerId,
                EffectiveBudgetAmount = budgetMoney.Amount,
                EffectiveBudgetCurrency = budgetMoney.Currency,
                RunningSpendAmount = runningSpendMoney.Amount,
                RunningSpendCurrency = runningSpendMoney.Currency,
                RemainingAmount = remainingMoney.Amount,
                RemainingCurrency = remainingMoney.Currency,
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
    /// cost, and updated running spend.
    /// </summary>
    private UsageCaptureResult BuildCaptureResult(
        EffectiveBudget budget,
        decimal cost,
        decimal runningSpend,
        string currency)
    {
        var callerId = this.GetPrimaryKeyString();

        decimal remainingAmount;
        string remainingCurrency;

        if (budget.HasBudget)
        {
            var budgetMoney = budget.Amount!;
            var runningSpendMoney = new Money(runningSpend, currency);
            var remainingMoney = budgetMoney - runningSpendMoney; // Throws on mismatch
            
            remainingAmount = remainingMoney.Amount;
            remainingCurrency = remainingMoney.Currency;
        }
        else
        {
            remainingAmount = 0m;
            remainingCurrency = currency;
        }

        return new UsageCaptureResult
        {
            CallerId = callerId,
            CostAmount = cost,
            CostCurrency = currency,
            RunningSpendAmount = runningSpend,
            RunningSpendCurrency = currency,
            RemainingAmount = remainingAmount,
            RemainingCurrency = remainingCurrency,
            BudgetSource = budget.Source,
            EffectiveGroupId = budget.GroupId,
        };
    }

    /// <summary>
    /// Builds a <see cref="UsageCaptureResult"/> from an existing
    /// <see cref="UsageEvent"/> (idempotent duplicate return).
    /// </summary>
    private UsageCaptureResult BuildCaptureResultFromEvent(UsageEvent evt)
    {
        return new UsageCaptureResult
        {
            CallerId = evt.CallerId.Value,
            CostAmount = evt.CostAmount,
            CostCurrency = evt.CostCurrency,
            RunningSpendAmount = evt.RunningSpendAfter,
            RunningSpendCurrency = evt.CostCurrency,
            RemainingAmount = 0m,
            RemainingCurrency = evt.CostCurrency,
            BudgetSource = evt.BudgetSource,
            EffectiveGroupId = evt.EffectiveGroupId,
        };
    }
}
