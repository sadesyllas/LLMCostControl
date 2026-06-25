using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// No-op implementation of <see cref="IPricingUpdatePublisher"/> for the admin
/// app. The admin app does not host Orleans and cannot publish stream events.
/// Pricing changes written by the admin app are picked up by the tracker's
/// refresh job on its next tick (§12.3, §12.4).
/// </summary>
public sealed class NullPricingUpdatePublisher : IPricingUpdatePublisher
{
    /// <summary>
    /// Does nothing — the admin app does not publish Orleans stream events.
    /// </summary>
    public Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
