using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>Stub pricing store for API integration tests.</summary>
public sealed class StubPricingStore : IPricingStore
{
    private readonly Dictionary<string, ModelPricing> _pricing = new(StringComparer.Ordinal);

    public void SetPricing(string model, ModelPricing pricing) => _pricing[model] = pricing;

    public Task<ModelPricing?> GetByModelAsync(string model, CancellationToken ct = default)
    {
        _pricing.TryGetValue(model, out var pricing);
        return Task.FromResult(pricing);
    }
}

/// <summary>Stub budget store for API integration tests.</summary>
public sealed class StubBudgetStore : IBudgetStore
{
    private readonly Dictionary<string, EffectiveBudget> _budgets = new(StringComparer.Ordinal);

    public void SetBudget(string callerId, EffectiveBudget budget) => _budgets[callerId] = budget;

    public Task<EffectiveBudget> ResolveAsync(CallerId callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        _budgets.TryGetValue(callerId.Value, out var budget);
        return Task.FromResult(budget ?? EffectiveBudget.None());
    }
}

/// <summary>Stub usage event store for API integration tests.</summary>
public sealed class StubUsageEventStore : IUsageEventStore
{
    private readonly Dictionary<string, UsageEvent> _events = new(StringComparer.Ordinal);

    /// <summary>Clears all stored events.</summary>
    public void Reset() => _events.Clear();

    public Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
        => Task.FromResult(_events.ContainsKey(eventId));

    public Task<UsageEvent?> GetByIdAsync(string eventId, CancellationToken ct = default)
    {
        _events.TryGetValue(eventId, out var evt);
        return Task.FromResult(evt);
    }

    public Task<bool> AppendAsync(UsageEvent evt, CancellationToken ct = default)
    {
        if (_events.ContainsKey(evt.EventId)) return Task.FromResult(false);
        _events[evt.EventId] = evt;
        return Task.FromResult(true);
    }

    public Task<List<UsageEvent>> GetForCallerAsync(CallerId callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        var results = _events.Values
            .Where(e => e.CallerId == callerId && e.Period == period)
            .OrderBy(e => e.CapturedAt)
            .ToList();
        return Task.FromResult(results);
    }
}

/// <summary>
/// Stub <see cref="IPricingStoreWriter"/> for M14 import endpoint tests.
/// Records calls and also updates a <see cref="StubPricingStore"/> so that
/// <c>PricingGrain</c> reads reflect the imported data after stream propagation.
/// </summary>
public sealed class StubPricingStoreWriter : IPricingStoreWriter
{
    private readonly StubPricingStore _store;

    /// <summary>Recorded (Provider, Entries) pairs from each ReplaceProviderAsync call.</summary>
    public List<(Provider Provider, IReadOnlyCollection<ModelPricing> Entries)> Calls { get; } = [];

    /// <summary>Creates the writer backed by the given store (must be the same instance registered as IPricingStore).</summary>
    public StubPricingStoreWriter(StubPricingStore store) => _store = store;

    /// <summary>Records the call and updates the backing store so grains see the new pricing.</summary>
    public Task ReplaceProviderAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        Calls.Add((provider, entries));
        foreach (var entry in entries)
            _store.SetPricing(entry.Model, entry);
        return Task.CompletedTask;
    }
}
