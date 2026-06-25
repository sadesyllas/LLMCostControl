using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// PostgreSQL-backed <see cref="IAdminQueryService"/>. Creates a short-lived
/// <see cref="CostTrackerDbContext"/> per query via <c>IDbContextFactory</c> and
/// reuses the shared resolution / repository logic so the admin views match the
/// tracker's behaviour exactly.
/// </summary>
public sealed class AdminQueryService : IAdminQueryService
{
    private readonly IDbContextFactory<CostTrackerDbContext> _contextFactory;

    /// <summary>Creates the service with the given context factory.</summary>
    public AdminQueryService(IDbContextFactory<CostTrackerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<CallerBudgetSummary> GetCallerSummaryAsync(string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        var period = BudgetPeriod.Current();

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var effective = await new BudgetResolutionRepository(db).ResolveAsync(caller, period, ct);
        var events = await new UsageEventRepository(db).GetForCallerAsync(caller, period, ct);

        var currency = effective.Amount?.Currency
            ?? events.Select(e => e.CostCurrency).FirstOrDefault()
            ?? "USD";
        var spent = events.Sum(e => e.CostAmount);
        var runningSpend = new Money(spent, currency);

        var remaining = effective.Amount is null ? null : effective.Amount.Subtract(runningSpend);

        return new CallerBudgetSummary(caller.Value, effective, runningSpend, remaining);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelPricing>> GetPricingAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var pricing = await new ModelPricingRepository(db).GetAllAsync(ct);
        return pricing
            .OrderBy(p => p.Provider)
            .ThenBy(p => p.Model)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageEvent>> GetRecentUsageAsync(string callerId, int limit = 20, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var events = await new UsageEventRepository(db).GetForCallerAsync(caller, BudgetPeriod.Current(), ct);
        return events
            .OrderByDescending(e => e.CapturedAt)
            .Take(limit)
            .ToList();
    }
}
