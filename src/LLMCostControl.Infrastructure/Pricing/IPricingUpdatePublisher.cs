using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Abstracts the publishing of <see cref="PricingUpdatedEvent"/> onto the
/// Orleans <c>pricing</c> stream. The Orleans implementation is wired in M8/M9;
/// this interface allows the refresh job to be tested without Orleans.
/// </summary>
public interface IPricingUpdatePublisher
{
    /// <summary>
    /// Publishes a pricing-updated event for the given provider and model names.
    /// </summary>
    Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default);
}
