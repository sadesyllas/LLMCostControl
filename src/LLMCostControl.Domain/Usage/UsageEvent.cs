using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Domain.Usage;

public class UsageEvent
{
    public string EventId { get; init; } = string.Empty;
    public CallerId CallerId { get; init; }
    public Guid? EffectiveGroupId { get; init; }
    public BudgetSource BudgetSource { get; init; }
    public string Model { get; init; } = string.Empty;
    public long TokensInput { get; init; }
    public long TokensOutput { get; init; }
    public long TokensCacheRead { get; init; }
    public long TokensCacheWrite { get; init; }
    public TokenPrices UnitPrices { get; init; }
    public decimal CostAmount { get; init; }
    public string CostCurrency { get; init; } = "USD";
    public decimal RunningSpendAfter { get; init; }
    public BudgetPeriod Period { get; init; }
    public DateTimeOffset CapturedAt { get; init; }

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
