using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// Production implementation of <see cref="IAdminReadService"/> backed by the
/// shared Infrastructure repositories.
/// </summary>
public sealed class AdminReadService : IAdminReadService
{
    private readonly BudgetResolutionRepository _budgetRepo;
    private readonly UsageEventRepository _usageRepo;
    private readonly ModelPricingRepository _pricingRepo;

    /// <summary>Creates the service with the required repositories.</summary>
    public AdminReadService(
        BudgetResolutionRepository budgetRepo,
        UsageEventRepository usageRepo,
        ModelPricingRepository pricingRepo)
    {
        _budgetRepo = budgetRepo;
        _usageRepo = usageRepo;
        _pricingRepo = pricingRepo;
    }

    /// <inheritdoc />
    public async Task<CallerBudgetSummary?> GetCallerSummaryAsync(
        string callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return null;

        var id = CallerId.From(callerId.Trim());
        var budget = await _budgetRepo.ResolveAsync(id, period, ct);
        var events = await _usageRepo.GetForCallerAsync(id, period, ct);

        var runningSpend = events.Count > 0 ? events[^1].RunningSpendAfter : 0m;
        var currency = events.Count > 0 ? events[^1].CostCurrency : "USD";

        return new CallerBudgetSummary
        {
            EffectiveBudget = budget,
            RunningSpend = runningSpend,
            Currency = currency,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelPricing>> GetAllPricingAsync(CancellationToken ct = default)
        => await _pricingRepo.GetAllAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageEvent>> GetRecentEventsAsync(
        string callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return [];
        var events = await _usageRepo.GetForCallerAsync(CallerId.From(callerId.Trim()), period, ct);
        return events.AsEnumerable().Reverse().ToList();
    }
}
