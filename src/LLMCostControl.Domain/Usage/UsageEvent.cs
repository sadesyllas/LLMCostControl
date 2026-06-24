using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Domain.Usage;

/// <summary>
/// An append-only audit row recording a single successful usage capture — i.e.
/// one LLM response whose cost was accrued against a caller.  Running spend can
/// be reconstructed by summing <see cref="CostAmount"/> per
/// <see cref="CallerId"/> per <see cref="Period"/>.
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

    /// <summary>
    /// The group whose budget was in effect for this capture, or null when the
    /// source was an override or none.
    /// </summary>
    public Guid? EffectiveGroupId { get; init; }

    /// <summary>Which budget source was in effect for the decision.</summary>
    public BudgetSource BudgetSource { get; init; }

    /// <summary>The model name reported by the gateway.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Non-cached input token count.</summary>
    public long TokensInput { get; init; }

    /// <summary>Generated output token count.</summary>
    public long TokensOutput { get; init; }

    /// <summary>Cached input token count (provider cache hit).</summary>
    public long TokensCacheRead { get; init; }

    /// <summary>Tokens written to the provider cache.</summary>
    public long TokensCacheWrite { get; init; }

    /// <summary>Snapshot of the unit prices used to compute the cost.</summary>
    public required TokenPrices UnitPrices { get; init; }

    /// <summary>The computed cost amount.</summary>
    public decimal CostAmount { get; init; }

    /// <summary>The currency of the cost amount.</summary>
    public string CostCurrency { get; init; } = "USD";

    /// <summary>The caller's running spend after this capture.</summary>
    public decimal RunningSpendAfter { get; init; }

    /// <summary>The budget period this capture falls in.</summary>
    public BudgetPeriod Period { get; init; }

    /// <summary>Server-side timestamp of the capture.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="UsageEvent"/> with all required fields, validating
    /// that ids, model, and token counts are well-formed.
    /// </summary>
    public static UsageEvent Create(
        string eventId,
        CallerId callerId,
        Guid? effectiveGroupId,
        BudgetSource budgetSource,
        string model,
        long tokensInput,
        long tokensOutput,
        long tokensCacheRead,
        long tokensCacheWrite,
        TokenPrices unitPrices,
        decimal costAmount,
        string costCurrency,
        decimal runningSpendAfter,
        BudgetPeriod period,
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

        return new UsageEvent
        {
            EventId = eventId,
            CallerId = callerId,
            EffectiveGroupId = effectiveGroupId,
            BudgetSource = budgetSource,
            Model = model.Trim(),
            TokensInput = tokensInput,
            TokensOutput = tokensOutput,
            TokensCacheRead = tokensCacheRead,
            TokensCacheWrite = tokensCacheWrite,
            UnitPrices = unitPrices,
            CostAmount = costAmount,
            CostCurrency = costCurrency,
            RunningSpendAfter = runningSpendAfter,
            Period = period,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
