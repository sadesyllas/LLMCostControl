using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Silo-local hosted service that subscribes to the Orleans <c>pricing</c> stream
/// and refreshes the local silo <see cref="IPricingCache"/> when an update is published.
/// </summary>
public sealed class PricingStreamSubscriber : IHostedService
{
    private readonly IClusterClient _clusterClient;
    private readonly IPricingCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PricingStreamSubscriber> _logger;
    private StreamSubscriptionHandle<PricingUpdatedStreamEvent>? _subscription;

    /// <summary>
    /// Creates the subscriber.
    /// </summary>
    /// <param name="clusterClient">The Orleans cluster client.</param>
    /// <param name="cache">The pricing cache to update.</param>
    /// <param name="scopeFactory">The service scope factory to resolve scoped stores.</param>
    /// <param name="logger">The logger instance.</param>
    public PricingStreamSubscriber(
        IClusterClient clusterClient,
        IPricingCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<PricingStreamSubscriber> logger)
    {
        _clusterClient = clusterClient;
        _cache = cache;
        _scopeFactory = scopeFactory;
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

                    _subscription = await stream.SubscribeAsync(async (evt, token) =>
                    {
                        _logger.LogInformation("Received pricing update stream event for {Count} models ({Provider}).", evt.UpdatedModels.Count, evt.Provider);
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var store = scope.ServiceProvider.GetRequiredService<IPricingStore>();
                            foreach (var model in evt.UpdatedModels)
                            {
                                var key = ProviderResolver.Key(evt.Provider, model);
                                try
                                {
                                    await _cache.RefreshAsync(key, evt.Provider, model, store);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to refresh cached pricing for {Key} on stream update.", key);
                                }
                            }
                        }
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
