using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Implementation of <see cref="IUserBudgetGrain"/> keyed by caller id
/// (typically an email). Resolves the effective budget for the caller
/// (per-user override wins; else largest group budget; else none), reconstructs
/// the running spend from the ledger on activation/rollover, and
/// answers check and capture calls (§7, §9.1, §9.3, §9.4).
/// </summary>
public sealed class UserBudgetGrain : Grain, IUserBudgetGrain, ITestUserBudgetGrain
{
    private const decimal TokensPerMillion = 1_000_000m;

    private readonly IBudgetStore _budgetStore;
    private readonly IUsageEventStore _usageEventStore;
    private readonly BudgetGrainOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ProviderInferenceMap _providerInference;

    // In-activation cache of the effective budgets.
    private EffectiveBudget? _cachedMonthlyBudget;
    private EffectiveBudget? _cachedWeeklyBudget;
    private DateTimeOffset _budgetLoadedAt;

    private string? _monthlyPeriodKey;
    private string? _monthlySpendLoadedKey;
    private decimal _monthlyRunningSpend;

    private string? _weeklyPeriodKey;
    private string? _weeklySpendLoadedKey;
    private decimal _weeklyRunningSpend;

    private string _runningSpendCurrency = "USD";
    private bool _hasCapturedAny;

    /// <summary>
    /// Creates the grain with the given budget store, usage event store,
    /// options, time provider, and provider inference map.
    /// </summary>
    public UserBudgetGrain(
        IBudgetStore budgetStore,
        IUsageEventStore usageEventStore,
        BudgetGrainOptions options,
        TimeProvider timeProvider,
        ProviderInferenceMap providerInference)
    {
        _budgetStore = budgetStore;
        _usageEventStore = usageEventStore;
        _options = options;
        _timeProvider = timeProvider;
        _providerInference = providerInference;
    }

    /// <summary>
    /// Reconstructs the running spend projection on grain activation by querying the ledger (§9.1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the activation operation.</returns>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await EnsurePeriodSpendLoadedAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// Checks whether the caller has remaining budget for a new request (§6.2.1).
    /// </summary>
    /// <returns>A budget check result containing details of whether the request is allowed.</returns>
    public async Task<BudgetCheckResult> CheckBudgetAsync()
    {
        await EnsurePeriodSpendLoadedAsync();
        var (monthly, weekly) = await GetEffectiveBudgetsAsync();
        return BuildCheckResult(monthly, weekly);
    }

    /// <summary>
    /// Captures token usage: appends to the ledger first, and then updates the in-memory projection (§9.3, §9.4).
    /// </summary>
    /// <param name="request">The usage capture request containing model, provider, tokens, and optional request ID.</param>
    /// <returns>A capture result containing remaining budgets and costs.</returns>
    public async Task<UsageCaptureResult> CaptureUsageAsync(UsageCaptureRequest request)
    {
        await EnsurePeriodSpendLoadedAsync();

        var eventId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString()
            : request.RequestId;

        // Idempotency check: if we already captured this requestId, return the original result from database.
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

        // Enforce same currency as existing running spend
        if (_hasCapturedAny && !_runningSpendCurrency.Equals(currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot combine money in different currencies: Pricing currency {currency} does not match existing running spend currency {_runningSpendCurrency}.");
        }

        // Construct period accruals. We use the updated values for runningSpendAfter, representing the projection if the event is appended.
        var accruals = new List<UsageEventPeriodAccrual>();

        if (monthly.HasBudget)
        {
            accruals.Add(UsageEventPeriodAccrual.Create(
                eventId: eventId,
                periodType: BudgetPeriodType.Monthly,
                periodKey: _monthlyPeriodKey ?? string.Empty,
                effectiveGroupId: monthly.GroupId,
                budgetSource: monthly.Source,
                effectiveBudgetAmount: monthly.Amount!,
                runningSpendAfter: _monthlyRunningSpend + cost));
        }

        if (weekly.HasBudget)
        {
            accruals.Add(UsageEventPeriodAccrual.Create(
                eventId: eventId,
                periodType: BudgetPeriodType.Weekly,
                periodKey: _weeklyPeriodKey ?? string.Empty,
                effectiveGroupId: weekly.GroupId,
                budgetSource: weekly.Source,
                effectiveBudgetAmount: weekly.Amount!,
                runningSpendAfter: _weeklyRunningSpend + cost));
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

        // Append first! Idempotency guard.
        var appended = await _usageEventStore.AppendAsync(usageEvent);
        if (!appended)
        {
            var existing = await _usageEventStore.GetByIdAsync(eventId);
            if (existing is not null)
            {
                return BuildCaptureResultFromEvent(existing);
            }
            throw new InvalidOperationException("Failed to append usage event but no existing event was found.");
        }

        // Update memory projection only after successful append to ledger!
        if (monthly.HasBudget)
        {
            _monthlyRunningSpend += cost;
        }
        if (weekly.HasBudget)
        {
            _weeklyRunningSpend += cost;
        }
        _runningSpendCurrency = currency;
        _hasCapturedAny = true;

        return BuildCaptureResult(monthly, weekly, cost, currency);
    }

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

    private async Task EnsurePeriodSpendLoadedAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var currentMonthly = BudgetPeriod.FromDate(now, BudgetPeriodType.Monthly);
        var currentWeekly = BudgetPeriod.FromDate(now, BudgetPeriodType.Weekly);

        var (monthly, weekly) = await GetEffectiveBudgetsAsync();
        var callerId = CallerId.From(this.GetPrimaryKeyString());

        // Load or rollover Monthly period spend
        if (monthly.HasBudget)
        {
            if (_monthlySpendLoadedKey != currentMonthly.Key)
            {
                var monthlyEvents = await _usageEventStore.GetForCallerAsync(callerId, currentMonthly, ct);
                _monthlyRunningSpend = monthlyEvents.Sum(e => e.CostAmount);
                _monthlyPeriodKey = currentMonthly.Key;
                _monthlySpendLoadedKey = currentMonthly.Key;
                if (monthlyEvents.Count > 0)
                {
                    _runningSpendCurrency = monthlyEvents[^1].CostCurrency;
                    _hasCapturedAny = true;
                }
                else if (!_hasCapturedAny)
                {
                    _runningSpendCurrency = monthly.Amount!.Currency;
                }
            }
        }
        else
        {
            _monthlyRunningSpend = 0m;
            _monthlyPeriodKey = currentMonthly.Key;
            _monthlySpendLoadedKey = null;
        }

        // Load or rollover Weekly period spend
        if (weekly.HasBudget)
        {
            if (_weeklySpendLoadedKey != currentWeekly.Key)
            {
                var weeklyEvents = await _usageEventStore.GetForCallerAsync(callerId, currentWeekly, ct);
                _weeklyRunningSpend = weeklyEvents.Sum(e => e.CostAmount);
                _weeklyPeriodKey = currentWeekly.Key;
                _weeklySpendLoadedKey = currentWeekly.Key;
                if (weeklyEvents.Count > 0)
                {
                    _runningSpendCurrency = weeklyEvents[^1].CostCurrency;
                    _hasCapturedAny = true;
                }
                else if (!_hasCapturedAny)
                {
                    _runningSpendCurrency = weekly.Amount!.Currency;
                }
            }
        }
        else
        {
            _weeklyRunningSpend = 0m;
            _weeklyPeriodKey = currentWeekly.Key;
            _weeklySpendLoadedKey = null;
        }
    }

    private async Task<(EffectiveBudget Monthly, EffectiveBudget Weekly)> GetEffectiveBudgetsAsync()
    {
        var now = _timeProvider.GetUtcNow();
        var currentMonthly = BudgetPeriod.FromDate(now, BudgetPeriodType.Monthly);
        var currentWeekly = BudgetPeriod.FromDate(now, BudgetPeriodType.Weekly);

        if (_cachedMonthlyBudget is not null &&
            _cachedWeeklyBudget is not null &&
            _monthlyPeriodKey == currentMonthly.Key &&
            _weeklyPeriodKey == currentWeekly.Key &&
            (now - _budgetLoadedAt) < _options.BudgetCacheTtl)
        {
            return (_cachedMonthlyBudget, _cachedWeeklyBudget);
        }

        var callerId = CallerId.From(this.GetPrimaryKeyString());

        _cachedMonthlyBudget = await _budgetStore.ResolveAsync(callerId, BudgetPeriodType.Monthly);
        _cachedWeeklyBudget = await _budgetStore.ResolveAsync(callerId, BudgetPeriodType.Weekly);

        _budgetLoadedAt = now;

        return (_cachedMonthlyBudget, _cachedWeeklyBudget);
    }

    private BudgetCheckResult BuildCheckResult(EffectiveBudget monthly, EffectiveBudget weekly)
    {
        var callerId = this.GetPrimaryKeyString();
        var budgets = new List<BudgetPeriodCheckResult>();
        bool allowed = true;
        bool hasAnyBudget = false;

        if (monthly.HasBudget)
        {
            hasAnyBudget = true;
            var budgetMoney = monthly.Amount!;
            var runningSpendMoney = new Money(_monthlyRunningSpend, _runningSpendCurrency);
            if (runningSpendMoney.IsZero && !_hasCapturedAny)
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
                PeriodKey = _monthlyPeriodKey ?? string.Empty,
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
            var runningSpendMoney = new Money(_weeklyRunningSpend, _runningSpendCurrency);
            if (runningSpendMoney.IsZero && !_hasCapturedAny)
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
                PeriodKey = _weeklyPeriodKey ?? string.Empty,
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

    private UsageCaptureResult BuildCaptureResult(
        EffectiveBudget monthly,
        EffectiveBudget weekly,
        decimal cost,
        string currency)
    {
        var callerId = this.GetPrimaryKeyString();
        var budgets = new List<BudgetPeriodCaptureResult>();

        if (monthly.HasBudget)
        {
            var budgetMoney = monthly.Amount!;
            var runningSpendMoney = new Money(_monthlyRunningSpend, currency);
            var remainingMoney = budgetMoney - runningSpendMoney;

            budgets.Add(new BudgetPeriodCaptureResult
            {
                Period = "Monthly",
                PeriodKey = _monthlyPeriodKey ?? string.Empty,
                RunningSpendAmount = _monthlyRunningSpend,
                RemainingAmount = remainingMoney.Amount
            });
        }

        if (weekly.HasBudget)
        {
            var budgetMoney = weekly.Amount!;
            var runningSpendMoney = new Money(_weeklyRunningSpend, currency);
            var remainingMoney = budgetMoney - runningSpendMoney;

            budgets.Add(new BudgetPeriodCaptureResult
            {
                Period = "Weekly",
                PeriodKey = _weeklyPeriodKey ?? string.Empty,
                RunningSpendAmount = _weeklyRunningSpend,
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

    /// <summary>
    /// Forcefully deactivates the grain activation on idle.
    /// </summary>
    public Task DeactivateOnIdleAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
