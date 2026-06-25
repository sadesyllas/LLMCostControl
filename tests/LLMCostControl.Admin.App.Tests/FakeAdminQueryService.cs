using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>In-memory <see cref="IAdminQueryService"/> for bUnit view tests.</summary>
public sealed class FakeAdminQueryService : IAdminQueryService
{
    public CallerBudgetSummary? Summary { get; set; }
    public List<ModelPricing> Pricing { get; } = [];
    public List<UsageEvent> Usage { get; } = [];

    public Task<CallerBudgetSummary> GetCallerSummaryAsync(string callerId, CancellationToken ct = default)
        => Task.FromResult(Summary
            ?? new CallerBudgetSummary(callerId, EffectiveBudget.None(), Money.Zero("USD"), null));

    public Task<IReadOnlyList<ModelPricing>> GetPricingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ModelPricing>>(Pricing);

    public Task<IReadOnlyList<UsageEvent>> GetRecentUsageAsync(string callerId, int limit = 20, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsageEvent>>(Usage);
}

/// <summary>
/// Recording <see cref="LLMCostControl.Infrastructure.Pricing.IPricingWriter"/>
/// for bUnit upload tests — captures provider writes without a database.
/// </summary>
public sealed class RecordingPricingWriter : LLMCostControl.Infrastructure.Pricing.IPricingWriter
{
    public List<Provider> Writes { get; } = [];

    public Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        Writes.Add(provider);
        return Task.CompletedTask;
    }
}
