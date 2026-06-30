using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Grains.Abstractions.StreamEvents;

/// <summary>
/// Stream event published onto the Orleans <c>pricing</c> stream after the
/// refresh job persists updated pricing for a provider (§8.6). Carries the
/// affected model names so each <c>PricingGrain</c> can decide whether to
/// reload its value.
/// </summary>
[GenerateSerializer]
public sealed record PricingUpdatedStreamEvent
{
    /// <summary>The provider whose pricing was updated.</summary>
    [Id(0)]
    public required Provider Provider { get; init; }

    /// <summary>The model names whose pricing changed in this update.</summary>
    [Id(1)]
    public required IReadOnlyList<string> UpdatedModels { get; init; }

    /// <summary>When the update was persisted.</summary>
    [Id(2)]
    public required DateTimeOffset UpdatedAt { get; init; }
}
