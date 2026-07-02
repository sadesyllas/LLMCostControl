using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Configuration for <see cref="PricingRefreshJob"/>: per-provider refresh
/// cadence with jitter, and the base tick interval for the periodic timer.
/// </summary>
public sealed class PricingRefreshOptions
{
    /// <summary>
    /// The base interval at which the job checks for due adapters.
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Default refresh cadence for a provider when not specified per-provider.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan DefaultCadence { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Default jitter applied to each provider's cadence, to stagger fetches.
    /// Default: up to 5 minutes.
    /// </summary>
    public TimeSpan DefaultJitter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Per-provider cadence overrides (provider name → cadence).</summary>
    public Dictionary<string, TimeSpan> ProviderCadences { get; set; } = new();

    /// <summary>Per-provider jitter overrides (provider name → jitter).</summary>
    public Dictionary<string, TimeSpan> ProviderJitters { get; set; } = new();

    /// <summary>
    /// Returns the refresh cadence for the given provider, falling back to
    /// <see cref="DefaultCadence"/>.
    /// </summary>
    public TimeSpan GetCadence(Provider provider)
    {
        var key = ProviderResolver.ToCanonicalString(provider);
        return ProviderCadences.TryGetValue(key, out var cadence) ? cadence : DefaultCadence;
    }

    /// <summary>
    /// Returns the jitter for the given provider, falling back to
    /// <see cref="DefaultJitter"/>.
    /// </summary>
    public TimeSpan GetJitter(Provider provider)
    {
        var key = ProviderResolver.ToCanonicalString(provider);
        return ProviderJitters.TryGetValue(key, out var jitter) ? jitter : DefaultJitter;
    }
}
