using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Repositories;
using Xunit;

namespace LLMCostControl.Infrastructure.Tests;

public class UsageEventRepositoryTests : RepositoryTestBase
{
    private async Task<Guid> SeedPricingAsync()
    {
        var repo = new ModelPricingRepository(Db);
        var pricing = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m));
        return await repo.InsertNewVersionAsync(pricing);
    }

    [Fact]
    public async Task Append_inserts_a_new_row()
    {
        var versionId = await SeedPricingAsync();
        var repo = new UsageEventRepository(Db);
        var evt = MakeUsageEvent("req-1", "alice@example.com", versionId);

        var inserted = await repo.AppendAsync(evt);

        inserted.Should().BeTrue();
        (await repo.ExistsAsync("req-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Append_with_duplicate_eventId_returns_false_and_does_not_insert()
    {
        var versionId = await SeedPricingAsync();
        var repo = new UsageEventRepository(Db);
        var evt = MakeUsageEvent("req-dup", "alice@example.com", versionId);
        await repo.AppendAsync(evt);

        var second = await repo.AppendAsync(evt);

        second.Should().BeFalse();
    }

    [Fact]
    public async Task GetForCaller_returns_events_for_that_caller_and_period()
    {
        var versionId = await SeedPricingAsync();
        var repo = new UsageEventRepository(Db);
        await repo.AppendAsync(MakeUsageEvent("r1", "alice@example.com", versionId, day: 1));
        await repo.AppendAsync(MakeUsageEvent("r2", "alice@example.com", versionId, day: 2));
        await repo.AppendAsync(MakeUsageEvent("r3", "bob@example.com", versionId));

        var events = await repo.GetForCallerAsync(
            CallerId.From("alice@example.com"),
            new BudgetPeriod(2026, 6));

        events.Should().HaveCount(2);
        events.Select(e => e.EventId).Should().BeInAscendingOrder();
    }

    private static UsageEvent MakeUsageEvent(string id, string caller, Guid pricingVersionId, int day = 15)
    {
        var capturedAt = new DateTimeOffset(2026, 6, day, 10, 0, 0, TimeSpan.Zero);
        var groupId = Guid.NewGuid();
        var accruals = new[]
        {
            UsageEventPeriodAccrual.Create(
                eventId: id,
                periodType: BudgetPeriodType.Monthly,
                periodKey: "2026-06",
                effectiveGroupId: groupId,
                budgetSource: BudgetSource.Group,
                effectiveBudgetAmount: new Money(500m, "USD"),
                runningSpendAfter: 12.35m)
        };

        return UsageEvent.Create(
            eventId: id,
            callerId: CallerId.From(caller),
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: 1000,
            tokensOutput: 500,
            tokensCacheRead: 200,
            tokensCacheWrite: 0,
            pricingVersionId: pricingVersionId,
            costAmount: 0.0125m,
            costCurrency: "USD",
            periodAccruals: accruals,
            capturedAt: capturedAt);
    }
}
