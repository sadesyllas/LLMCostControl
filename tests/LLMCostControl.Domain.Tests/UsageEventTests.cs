using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;

namespace LLMCostControl.Domain.Tests;

public class UsageEventTests
{
    [Fact]
    public void Create_sets_all_fields_correctly()
    {
        var callerId = CallerId.From("bob@example.com");
        var groupId = Guid.NewGuid();
        var period = new BudgetPeriod(2026, 6);
        var prices = TokenPrices.Create(2.5m, 10m, 1.25m);
        var capturedAt = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        var evt = UsageEvent.Create(
            eventId: "req-123",
            callerId: callerId,
            effectiveGroupId: groupId,
            budgetSource: BudgetSource.Group,
            model: "gpt-4o",
            tokensInput: 1000,
            tokensOutput: 500,
            tokensCacheRead: 200,
            tokensCacheWrite: 0,
            unitPrices: prices,
            costAmount: 0.0125m,
            costCurrency: "USD",
            runningSpendAfter: 12.35m,
            period: period,
            capturedAt: capturedAt);

        evt.EventId.Should().Be("req-123");
        evt.CallerId.Should().Be(callerId);
        evt.EffectiveGroupId.Should().Be(groupId);
        evt.BudgetSource.Should().Be(BudgetSource.Group);
        evt.Model.Should().Be("gpt-4o");
        evt.TokensInput.Should().Be(1000);
        evt.TokensOutput.Should().Be(500);
        evt.TokensCacheRead.Should().Be(200);
        evt.TokensCacheWrite.Should().Be(0);
        evt.UnitPrices.Should().Be(prices);
        evt.CostAmount.Should().Be(0.0125m);
        evt.CostCurrency.Should().Be("USD");
        evt.RunningSpendAfter.Should().Be(12.35m);
        evt.Period.Should().Be(period);
        evt.CapturedAt.Should().Be(capturedAt);
    }

    [Fact]
    public void Create_rejects_empty_event_id()
    {
        var act = () => UsageEvent.Create(
            eventId: "",
            callerId: CallerId.From("bob@example.com"),
            effectiveGroupId: null,
            budgetSource: BudgetSource.None,
            model: "gpt-4o",
            tokensInput: 0, tokensOutput: 0, tokensCacheRead: 0, tokensCacheWrite: 0,
            unitPrices: TokenPrices.Create(1m, 1m),
            costAmount: 0m, costCurrency: "USD", runningSpendAfter: 0m,
            period: new BudgetPeriod(2026, 6));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_negative_token_counts()
    {
        var act = () => UsageEvent.Create(
            eventId: "req-1",
            callerId: CallerId.From("bob@example.com"),
            effectiveGroupId: null,
            budgetSource: BudgetSource.None,
            model: "gpt-4o",
            tokensInput: -1, tokensOutput: 0, tokensCacheRead: 0, tokensCacheWrite: 0,
            unitPrices: TokenPrices.Create(1m, 1m),
            costAmount: 0m, costCurrency: "USD", runningSpendAfter: 0m,
            period: new BudgetPeriod(2026, 6));

        act.Should().Throw<ArgumentException>();
    }
}
