using System;
using System.Collections.Generic;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using FluentAssertions;
using Xunit;

namespace LLMCostControl.Domain.Tests;

public class UsageEventTests
{
    [Fact]
    public void Create_sets_all_fields_correctly()
    {
        var callerId = CallerId.From("bob@example.com");
        var groupId = Guid.NewGuid();
        var prices = TokenPrices.Create(2.5m, 10m, 1.25m);
        var capturedAt = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        var accruals = new[]
        {
            UsageEventPeriodAccrual.Create(
                eventId: "req-123",
                periodType: BudgetPeriodType.Monthly,
                periodKey: "2026-06",
                effectiveGroupId: groupId,
                budgetSource: BudgetSource.Group,
                effectiveBudgetAmount: new Money(500.00m, "USD"),
                runningSpendAfter: 12.35m)
        };

        var evt = UsageEvent.Create(
            eventId: "req-123",
            callerId: callerId,
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: 1000,
            tokensOutput: 500,
            tokensCacheRead: 200,
            tokensCacheWrite: 0,
            unitPrices: prices,
            costAmount: 0.0125m,
            costCurrency: "USD",
            periodAccruals: accruals,
            capturedAt: capturedAt);

        evt.EventId.Should().Be("req-123");
        evt.CallerId.Should().Be(callerId);
        evt.Model.Should().Be("gpt-4o");
        evt.Provider.Should().Be(Provider.OpenAI);
        evt.TokensInput.Should().Be(1000);
        evt.TokensOutput.Should().Be(500);
        evt.TokensCacheRead.Should().Be(200);
        evt.TokensCacheWrite.Should().Be(0);
        evt.UnitPrices.Should().Be(prices);
        evt.CostAmount.Should().Be(0.0125m);
        evt.CostCurrency.Should().Be("USD");
        evt.PeriodAccruals.Should().ContainSingle();
        evt.PeriodAccruals[0].PeriodType.Should().Be(BudgetPeriodType.Monthly);
        evt.PeriodAccruals[0].PeriodKey.Should().Be("2026-06");
        evt.PeriodAccruals[0].EffectiveGroupId.Should().Be(groupId);
        evt.PeriodAccruals[0].BudgetSource.Should().Be(BudgetSource.Group);
        evt.PeriodAccruals[0].EffectiveBudgetAmount.Amount.Should().Be(500.00m);
        evt.PeriodAccruals[0].RunningSpendAfter.Should().Be(12.35m);
        evt.CapturedAt.Should().Be(capturedAt);
    }

    [Fact]
    public void Create_rejects_empty_event_id()
    {
        var act = () => UsageEvent.Create(
            eventId: "",
            callerId: CallerId.From("bob@example.com"),
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: 0, tokensOutput: 0, tokensCacheRead: 0, tokensCacheWrite: 0,
            unitPrices: TokenPrices.Create(1m, 1m),
            costAmount: 0m, costCurrency: "USD",
            periodAccruals: Array.Empty<UsageEventPeriodAccrual>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_negative_token_counts()
    {
        var act = () => UsageEvent.Create(
            eventId: "req-1",
            callerId: CallerId.From("bob@example.com"),
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: -1, tokensOutput: 0, tokensCacheRead: 0, tokensCacheWrite: 0,
            unitPrices: TokenPrices.Create(1m, 1m),
            costAmount: 0m, costCurrency: "USD",
            periodAccruals: Array.Empty<UsageEventPeriodAccrual>());

        act.Should().Throw<ArgumentException>();
    }
}
