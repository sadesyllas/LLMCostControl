using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Admin.App.Services;

/// <summary>
/// No-op <see cref="IPricingUpdatePublisher"/> for the admin app. The admin app
/// does not host Orleans, so it cannot publish onto the <c>pricing</c> stream
/// (§12.3): a pricing-file upload writes to PostgreSQL via the shared import
/// path, and pricing grains pick up the change on their next store reload
/// (30 s TTL) / refresh tick. The stream event is intentionally not published here.
/// </summary>
public sealed class NoOpPricingUpdatePublisher : IPricingUpdatePublisher
{
    /// <inheritdoc />
    public Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
        => Task.CompletedTask;
}
