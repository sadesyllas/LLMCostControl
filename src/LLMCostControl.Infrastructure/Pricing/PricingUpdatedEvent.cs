using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Event published by the pricing refresh job after successfully persisting
/// updated pricing for a provider. Consumed by <c>PricingGrain</c> activations
/// via the Orleans <c>pricing</c> stream (§8.6).
/// </summary>
public sealed record PricingUpdatedEvent
{
    /// <summary>The provider whose pricing was updated.</summary>
    public required Provider Provider { get; init; }

    /// <summary>The model names whose pricing changed in this update.</summary>
    public required IReadOnlyList<string> UpdatedModels { get; init; }

    /// <summary>When the update was persisted.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
