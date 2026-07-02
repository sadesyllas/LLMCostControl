using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Background job (§8.4) that keeps pricing warm. Iterates all registered
/// <see cref="IPricingAdapter"/>s, persists their results to PostgreSQL via
/// <see cref="ModelPricingRepository"/>, and publishes
/// <see cref="PricingUpdatedEvent"/>s. This is the **only** writer of pricing
/// data — adapters do not write to the DB directly.
/// </summary>
public sealed class PricingRefreshJob : BackgroundService
{
    private readonly IReadOnlyList<IPricingAdapter> _adapters;
    private readonly ModelPricingRepository _repository;
    private readonly IPricingUpdatePublisher _publisher;
    private readonly PricingRefreshOptions _options;
    private readonly ILogger<PricingRefreshJob> _logger;
    private readonly Dictionary<Provider, DateTimeOffset> _lastFetchTimes = new();
    private int _isRunning;

    /// <summary>
    /// Creates the refresh job.
    /// </summary>
    public PricingRefreshJob(
        IEnumerable<IPricingAdapter> adapters,
        ModelPricingRepository repository,
        IPricingUpdatePublisher publisher,
        PricingRefreshOptions options,
        ILogger<PricingRefreshJob> logger)
    {
        _adapters = adapters.ToList();
        _repository = repository;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Runs the refresh loop: on each tick, iterates adapters whose per-provider
    /// cadence (plus jitter) has elapsed, fetches, persists, and publishes.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pricing refresh job started with {Count} adapters.", _adapters.Count);

        using var timer = new PeriodicTimer(_options.TickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshDueAdaptersAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Iterates all adapters and refreshes those whose cadence has elapsed.
    /// Singularity guard: returns immediately if a previous tick is still
    /// running.
    /// </summary>
    public async Task RefreshDueAdaptersAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            _logger.LogWarning("Pricing refresh tick skipped: a previous tick is still running.");
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var adapter in _adapters)
            {
                if (!IsDue(adapter.Provider, now))
                {
                    continue;
                }

                await RefreshProviderAsync(adapter, ct);
                _lastFetchTimes[adapter.Provider] = now;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    /// <summary>
    /// Forces an immediate refresh of all adapters regardless of cadence.
    /// Useful for the localhost file import endpoint (M14).
    /// </summary>
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            _logger.LogWarning("RefreshAll skipped: a refresh is already running.");
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var adapter in _adapters)
            {
                await RefreshProviderAsync(adapter, ct);
                _lastFetchTimes[adapter.Provider] = now;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private async Task RefreshProviderAsync(IPricingAdapter adapter, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Refreshing pricing for provider {Provider}.", adapter.Provider);

            var entries = await adapter.FetchAsync(ct);

            if (entries.Count == 0)
            {
                _logger.LogWarning("Adapter for {Provider} returned no entries.", adapter.Provider);
                return;
            }

            var changed = await _repository.ReplaceProviderPricingAsync(adapter.Provider, entries, ct);

            if (changed.Count > 0)
            {
                await _publisher.PublishAsync(adapter.Provider, changed, ct);
            }

            _logger.LogInformation(
                "Refreshed {Count} model(s) for provider {Provider} ({ChangedCount} changed).",
                entries.Count,
                adapter.Provider,
                changed.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing pricing for provider {Provider}.", adapter.Provider);
        }
    }

    private bool IsDue(Provider provider, DateTimeOffset now)
    {
        if (!_lastFetchTimes.TryGetValue(provider, out var lastFetch))
        {
            return true;
        }

        var cadence = _options.GetCadence(provider);
        var jitter = _options.GetJitter(provider);
        var elapsed = now - lastFetch;

        return elapsed >= cadence + jitter;
    }
}
