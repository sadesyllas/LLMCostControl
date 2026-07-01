using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Domain.Usage;

/// <summary>
/// An append-only audit row recording a single successful usage capture — i.e.
/// be reconstructed by summing <see cref="CostAmount"/> per
/// <see cref="CallerId"/> per <see cref="PeriodAccruals"/>.
/// </summary>
public class UsageEvent
{
    /// <summary>
    /// Unique event id; when the gateway supplies a <c>requestId</c> it is used
    /// here as the idempotency / natural key.
    /// </summary>
    public string EventId { get; init; } = string.Empty;

    /// <summary>The caller whose spend was accrued.</summary>
    public CallerId CallerId { get; init; }



    /// <summary>The model name reported by the gateway.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// The provider that priced the call, resolved per §6.2.3. Recorded so the
    /// append-only ledger is self-describing when a model name is shared across
    /// providers (§8.5).
    /// </summary>
    public Provider Provider { get; init; }

    /// <summary>Non-cached input token count.</summary>
    public long TokensInput { get; init; }

    /// <summary>Generated output token count.</summary>
    public long TokensOutput { get; init; }

    /// <summary>Cached input token count (provider cache hit).</summary>
    public long TokensCacheRead { get; init; }

    /// <summary>Tokens written to the provider cache.</summary>
    public long TokensCacheWrite { get; init; }

    /// <summary>The ID of the model pricing version used to price this capture (§9.4, M23).</summary>
    public Guid PricingVersionId { get; init; }

    /// <summary>The computed cost amount.</summary>
    public decimal CostAmount { get; init; }

    /// <summary>The currency of the cost amount.</summary>
    public string CostCurrency { get; init; } = "USD";

    /// <summary>The period accruals associated with this event.</summary>
    public List<UsageEventPeriodAccrual> PeriodAccruals { get; init; } = [];

    /// <summary>Server-side timestamp of the capture.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="UsageEvent"/> with all required fields, validating
    /// that ids, model, and token counts are well-formed.
    /// </summary>
    public static UsageEvent Create(
        string eventId,
        CallerId callerId,
        string model,
        Provider provider,
        long tokensInput,
        long tokensOutput,
        long tokensCacheRead,
        long tokensCacheWrite,
        Guid pricingVersionId,
        decimal costAmount,
        string costCurrency,
        IReadOnlyCollection<UsageEventPeriodAccrual> periodAccruals,
        DateTimeOffset? capturedAt = null)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Event id cannot be empty.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(costCurrency))
        {
            throw new ArgumentException("Cost currency cannot be empty.", nameof(costCurrency));
        }

        if (tokensInput < 0 || tokensOutput < 0 || tokensCacheRead < 0 || tokensCacheWrite < 0)
        {
            throw new ArgumentException("Token counts cannot be negative.");
        }

        if (pricingVersionId == Guid.Empty)
        {
            throw new ArgumentException("Pricing version ID must be a valid non-empty GUID.", nameof(pricingVersionId));
        }

        return new UsageEvent
        {
            EventId = eventId,
            CallerId = callerId,
            Model = model.Trim(),
            Provider = provider,
            TokensInput = tokensInput,
            TokensOutput = tokensOutput,
            TokensCacheRead = tokensCacheRead,
            TokensCacheWrite = tokensCacheWrite,
            PricingVersionId = pricingVersionId,
            CostAmount = costAmount,
            CostCurrency = costCurrency,
            PeriodAccruals = periodAccruals.ToList(),
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
