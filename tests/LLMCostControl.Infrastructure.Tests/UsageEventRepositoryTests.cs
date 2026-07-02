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

    [Fact]
    public async Task GetForCaller_reconstructs_both_monthly_and_weekly_accruals_over_postgres()
    {
        var versionId = await SeedPricingAsync();
        var repo = new UsageEventRepository(Db);

        var capturedAt1 = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero); // Monday, June 15, 2026 (W25)
        var capturedAt2 = new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero); // Thursday, June 25, 2026 (W26)

        var monthlyPeriod = new BudgetPeriod(2026, 6);
        var weeklyPeriod1 = BudgetPeriod.FromDate(capturedAt1, BudgetPeriodType.Weekly);
        var weeklyPeriod2 = BudgetPeriod.FromDate(capturedAt2, BudgetPeriodType.Weekly);

        var groupId = Guid.NewGuid();
        var accruals1 = new[]
        {
            UsageEventPeriodAccrual.Create(
                eventId: "multi-r1",
                periodType: BudgetPeriodType.Monthly,
                periodKey: monthlyPeriod.Key,
                effectiveGroupId: groupId,
                budgetSource: BudgetSource.Group,
                effectiveBudgetAmount: new Money(500m, "USD"),
                runningSpendAfter: 12.35m),
            UsageEventPeriodAccrual.Create(
                eventId: "multi-r1",
                periodType: BudgetPeriodType.Weekly,
                periodKey: weeklyPeriod1.Key,
                effectiveGroupId: groupId,
                budgetSource: BudgetSource.Group,
                effectiveBudgetAmount: new Money(100m, "USD"),
                runningSpendAfter: 5.20m)
        };

        var evt1 = UsageEvent.Create(
            eventId: "multi-r1",
            callerId: CallerId.From("multi@example.com"),
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: 1000,
            tokensOutput: 500,
            tokensCacheRead: 0,
            tokensCacheWrite: 0,
            pricingVersionId: versionId,
            costAmount: 0.0125m,
            costCurrency: "USD",
            periodAccruals: accruals1,
            capturedAt: capturedAt1);

        var accruals2 = new[]
        {
            UsageEventPeriodAccrual.Create(
                eventId: "multi-r2",
                periodType: BudgetPeriodType.Monthly,
                periodKey: monthlyPeriod.Key,
                effectiveGroupId: null,
                budgetSource: BudgetSource.UserOverride,
                effectiveBudgetAmount: new Money(1000m, "USD"),
                runningSpendAfter: 24.70m),
            UsageEventPeriodAccrual.Create(
                eventId: "multi-r2",
                periodType: BudgetPeriodType.Weekly,
                periodKey: weeklyPeriod2.Key,
                effectiveGroupId: null,
                budgetSource: BudgetSource.UserOverride,
                effectiveBudgetAmount: new Money(200m, "USD"),
                runningSpendAfter: 8.40m)
        };

        var evt2 = UsageEvent.Create(
            eventId: "multi-r2",
            callerId: CallerId.From("multi@example.com"),
            model: "gpt-4o",
            provider: Provider.OpenAI,
            tokensInput: 500,
            tokensOutput: 250,
            tokensCacheRead: 0,
            tokensCacheWrite: 0,
            pricingVersionId: versionId,
            costAmount: 0.00625m,
            costCurrency: "USD",
            periodAccruals: accruals2,
            capturedAt: capturedAt2);

        await repo.AppendAsync(evt1);
        await repo.AppendAsync(evt2);

        // Retrieve Monthly events (both are in June 2026)
        var monthlyEvents = await repo.GetForCallerAsync(
            CallerId.From("multi@example.com"),
            monthlyPeriod);

        monthlyEvents.Should().HaveCount(2);
        monthlyEvents.Select(e => e.EventId).Should().BeEquivalentTo("multi-r1", "multi-r2");

        var monthlyEvt1 = monthlyEvents.First(e => e.EventId == "multi-r1");
        monthlyEvt1.PeriodAccruals.Should().ContainSingle(a => a.PeriodType == BudgetPeriodType.Monthly);
        var monthlyAccrual1 = monthlyEvt1.PeriodAccruals.Single(a => a.PeriodType == BudgetPeriodType.Monthly);
        monthlyAccrual1.PeriodKey.Should().Be(monthlyPeriod.Key);
        monthlyAccrual1.EffectiveGroupId.Should().Be(groupId);
        monthlyAccrual1.BudgetSource.Should().Be(BudgetSource.Group);
        monthlyAccrual1.EffectiveBudgetAmount.Amount.Should().Be(500m);
        monthlyAccrual1.RunningSpendAfter.Should().Be(12.35m);

        var monthlyEvt2 = monthlyEvents.First(e => e.EventId == "multi-r2");
        monthlyEvt2.PeriodAccruals.Should().ContainSingle(a => a.PeriodType == BudgetPeriodType.Monthly);
        var monthlyAccrual2 = monthlyEvt2.PeriodAccruals.Single(a => a.PeriodType == BudgetPeriodType.Monthly);
        monthlyAccrual2.PeriodKey.Should().Be(monthlyPeriod.Key);
        monthlyAccrual2.EffectiveGroupId.Should().BeNull();
        monthlyAccrual2.BudgetSource.Should().Be(BudgetSource.UserOverride);
        monthlyAccrual2.EffectiveBudgetAmount.Amount.Should().Be(1000m);
        monthlyAccrual2.RunningSpendAfter.Should().Be(24.70m);

        // Retrieve Weekly events for W25 (only multi-r1 should return)
        var weeklyEvents1 = await repo.GetForCallerAsync(
            CallerId.From("multi@example.com"),
            weeklyPeriod1);

        weeklyEvents1.Should().HaveCount(1);
        weeklyEvents1[0].EventId.Should().Be("multi-r1");
        weeklyEvents1[0].PeriodAccruals.Should().ContainSingle(a => a.PeriodType == BudgetPeriodType.Weekly);
        var weeklyAccrual1 = weeklyEvents1[0].PeriodAccruals.Single(a => a.PeriodType == BudgetPeriodType.Weekly);
        weeklyAccrual1.PeriodKey.Should().Be(weeklyPeriod1.Key);
        weeklyAccrual1.EffectiveGroupId.Should().Be(groupId);
        weeklyAccrual1.BudgetSource.Should().Be(BudgetSource.Group);
        weeklyAccrual1.EffectiveBudgetAmount.Amount.Should().Be(100m);
        weeklyAccrual1.RunningSpendAfter.Should().Be(5.20m);

        // Retrieve Weekly events for W26 (only multi-r2 should return)
        var weeklyEvents2 = await repo.GetForCallerAsync(
            CallerId.From("multi@example.com"),
            weeklyPeriod2);

        weeklyEvents2.Should().HaveCount(1);
        weeklyEvents2[0].EventId.Should().Be("multi-r2");
        weeklyEvents2[0].PeriodAccruals.Should().ContainSingle(a => a.PeriodType == BudgetPeriodType.Weekly);
        var weeklyAccrual2 = weeklyEvents2[0].PeriodAccruals.Single(a => a.PeriodType == BudgetPeriodType.Weekly);
        weeklyAccrual2.PeriodKey.Should().Be(weeklyPeriod2.Key);
        weeklyAccrual2.EffectiveGroupId.Should().BeNull();
        weeklyAccrual2.BudgetSource.Should().Be(BudgetSource.UserOverride);
        weeklyAccrual2.EffectiveBudgetAmount.Amount.Should().Be(200m);
        weeklyAccrual2.RunningSpendAfter.Should().Be(8.40m);
    }
}
