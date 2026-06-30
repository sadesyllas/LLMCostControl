using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using LLMCostControl.Infrastructure.Pricing;
using Orleans.Streams;

namespace LLMCostControl.Grains.Publishers;

/// <summary>
/// Orleans-based implementation of <see cref="IPricingUpdatePublisher"/> that
/// publishes <see cref="PricingUpdatedStreamEvent"/> onto the Orleans
/// <c>pricing</c> stream. Consumed by <c>PricingGrain</c> activations (§8.6).
/// </summary>
public sealed class OrleansPricingPublisher : IPricingUpdatePublisher
{
    private readonly IClusterClient _clusterClient;

    /// <summary>Creates the publisher with the given cluster client (for stream access).</summary>
    public OrleansPricingPublisher(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Publishes a pricing-updated event onto the <c>pricing</c> stream.
    /// </summary>
    public async Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
    {
        var streamProvider = _clusterClient.GetStreamProvider("pricing");
        var stream = streamProvider.GetStream<PricingUpdatedStreamEvent>("pricing", "updates");

        var evt = new PricingUpdatedStreamEvent
        {
            Provider = provider,
            UpdatedModels = updatedModels,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await stream.OnNextAsync(evt);
    }
}
