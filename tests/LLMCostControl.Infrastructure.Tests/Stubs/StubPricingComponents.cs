using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Infrastructure.Tests;

/// <summary>
/// Stub adapter that returns a fixed set of pricing entries, recording how
/// many times it was called.
/// </summary>
public class StubPricingAdapter : IPricingAdapter
{
    private readonly IReadOnlyCollection<ModelPricing> _entries;
    private int _callCount;

    /// <summary>The provider this adapter handles.</summary>
    public Provider Provider { get; }

    /// <summary>Number of times <see cref="FetchAsync"/> was called.</summary>
    public int CallCount => _callCount;

    /// <summary>Creates a stub adapter returning the given entries.</summary>
    public StubPricingAdapter(Provider provider, IReadOnlyCollection<ModelPricing> entries)
    {
        Provider = provider;
        _entries = entries;
    }

    /// <summary>Returns the fixed entries and increments the call counter.</summary>
    public Task<IReadOnlyCollection<ModelPricing>> FetchAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_entries);
    }
}

/// <summary>
/// Stub publisher that records all published events.
/// </summary>
public class StubPricingUpdatePublisher : IPricingUpdatePublisher
{
    private readonly List<(Provider Provider, IReadOnlyList<string> Models)> _published = new();

    /// <summary>All events published so far.</summary>
    public IReadOnlyList<(Provider Provider, IReadOnlyList<string> Models)> Published => _published;

    /// <summary>Records the event.</summary>
    public Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
    {
        _published.Add((provider, updatedModels));
        return Task.CompletedTask;
    }
}
