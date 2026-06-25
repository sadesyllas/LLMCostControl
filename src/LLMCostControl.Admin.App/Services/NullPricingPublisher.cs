using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// No-op implementation of <see cref="IPricingUpdatePublisher"/> used in the
/// Admin.App, which does not host Orleans. After a pricing file import, the
/// Tracker API's next pricing refresh tick propagates the DB changes to the
/// pricing grains.
/// </summary>
public sealed class NullPricingPublisher : IPricingUpdatePublisher
{
    /// <inheritdoc />
    public Task PublishAsync(
        Provider provider,
        IReadOnlyList<string> updatedModels,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
