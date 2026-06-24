using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Common base class for provider pricing adapters. Implements the hybrid
/// strategy (§8.2): each concrete adapter provides a <see cref="FetchLiveAsync"/>
/// that fetches current pricing from the provider's published source. On
/// failure (or no live source), the base class falls back to the most recently
/// persisted DB values and stamps them with a <see cref="ModelPricing.StaleSince"/>
/// staleness signal.
/// </summary>
public abstract class PricingAdapterBase : IPricingAdapter
{
    private readonly ModelPricingRepository _repository;

    /// <summary>The provider this adapter handles.</summary>
    public abstract Provider Provider { get; }

    /// <summary>
    /// Creates the adapter with the given repository for fallback reads.
    /// </summary>
    protected PricingAdapterBase(ModelPricingRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Fetches current pricing: attempts a live fetch, and on failure falls
    /// back to persisted values with a staleness signal.
    /// </summary>
    public async Task<IReadOnlyCollection<ModelPricing>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            return await FetchLiveAsync(ct);
        }
        catch (Exception)
        {
            return await FallbackToPersistedAsync(ct);
        }
    }

    /// <summary>
    /// Implemented by each concrete adapter to fetch live pricing from the
    /// provider's source. Should throw on any failure; the base class handles
    /// the fallback.
    /// </summary>
    protected abstract Task<IReadOnlyCollection<ModelPricing>> FetchLiveAsync(CancellationToken ct);

    private async Task<IReadOnlyCollection<ModelPricing>> FallbackToPersistedAsync(CancellationToken ct)
    {
        var persisted = await _repository.GetByProviderAsync(Provider, ct);
        var staleSince = DateTimeOffset.UtcNow;

        foreach (var entry in persisted)
        {
            entry.StaleSince = staleSince;
        }

        return persisted;
    }
}
