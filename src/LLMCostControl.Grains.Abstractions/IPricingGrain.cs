namespace LLMCostControl.Grains.Abstractions;


/// <summary>
/// Grain interface keyed by model name. Holds the model's current unit prices
/// in-silo so the capture path can compute cost without a DB round-trip (§8.6).
/// Implemented as a <c>[StatelessWorker]</c> local grain.
/// </summary>
public interface IPricingGrain : IGrainWithStringKey
{
    /// <summary>
    /// Returns the current pricing for this model, or null when the model is
    /// unknown / not in the allowed set.
    /// </summary>
    Task<PricingResult?> GetPricingAsync();
}
