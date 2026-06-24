using LLMCostControl.Grains.Abstractions;
using Orleans.Runtime;

namespace LLMCostControl.Grains.Implementations;

/// <summary>
/// Minimal stub implementation of <see cref="IPricingGrain"/> for M8. Returns
/// placeholder pricing. The real implementation (with stream subscription,
/// StatelessWorker placement, DB-seeded values) is built in M9.
/// </summary>
public sealed class PricingGrain : Grain, IPricingGrain
{
    /// <summary>
    /// Returns placeholder pricing for M8 silo bootstrap verification.
    /// </summary>
    public Task<PricingResult?> GetPricingAsync()
    {
        var result = new PricingResult
        {
            Input = 2.5m,
            Output = 10m,
            CacheRead = 1.25m,
            CacheWrite = null,
            Currency = "USD",
            IsStale = false,
        };

        return Task.FromResult<PricingResult?>(result);
    }
}
