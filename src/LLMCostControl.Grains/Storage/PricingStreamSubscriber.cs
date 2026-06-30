using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Silo-local hosted service that subscribes to the Orleans <c>pricing</c> stream
/// and invalidates the local silo <see cref="IPricingCache"/> when an update is published.
/// </summary>
public sealed class PricingStreamSubscriber : IHostedService
{
    private readonly IClusterClient _clusterClient;
    private readonly IPricingCache _cache;
    private readonly ILogger<PricingStreamSubscriber> _logger;
    private StreamSubscriptionHandle<PricingUpdatedStreamEvent>? _subscription;

    /// <summary>
    /// Creates the subscriber.
    /// </summary>
    /// <param name="clusterClient">The Orleans cluster client.</param>
    /// <param name="cache">The pricing cache to invalidate.</param>
    /// <param name="logger">The logger instance.</param>
    public PricingStreamSubscriber(
        IClusterClient clusterClient,
        IPricingCache cache,
        ILogger<PricingStreamSubscriber> logger)
    {
        _clusterClient = clusterClient;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Starts the hosted service and subscribes to the stream in a background task to prevent blocking silo startup.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the startup operation.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PricingStreamSubscriber starting...");
        
        // Run subscription in a background task to not block silo startup sequence
        _ = Task.Run(async () =>
        {
            // Retry subscription if it fails (e.g. Orleans provider not ready yet)
            var subscribed = false;
            while (!subscribed && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var streamProvider = _clusterClient.GetStreamProvider("pricing");
                    var stream = streamProvider.GetStream<PricingUpdatedStreamEvent>("pricing", "updates");

                    _subscription = await stream.SubscribeAsync((evt, token) =>
                    {
                        _logger.LogInformation("Received pricing update stream event for {Count} models ({Provider}).", evt.UpdatedModels.Count, evt.Provider);
                        foreach (var model in evt.UpdatedModels)
                        {
                            // The event carries the provider, so invalidate the exact
                            // per-(provider, model) composite cache key (§8.6).
                            _cache.Remove(ProviderResolver.Key(evt.Provider, model));
                        }
                        return Task.CompletedTask;
                    });

                    subscribed = true;
                    _logger.LogInformation("PricingStreamSubscriber successfully subscribed to pricing stream.");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to subscribe to pricing stream, retrying...");
                    try
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the hosted service and unsubscribes from the stream if active.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the shutdown operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            try
            {
                await _subscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from pricing stream during shutdown.");
            }
        }
    }
}
