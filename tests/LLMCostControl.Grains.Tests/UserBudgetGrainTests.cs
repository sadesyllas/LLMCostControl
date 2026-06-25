using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMostControl.Grains.Tests;

namespace LLMCostControl.Grains.Tests;

/// <summary>
/// Tests for <see cref="UserBudgetGrain"/> covering effective-budget
/// resolution rules, TTL caching, fail-closed / AllowNonBudgetedUsers, and
/// period rollover (M10 acceptance criteria).
/// </summary>
public class UserBudgetGrainTests : GrainTestBase
{
    public UserBudgetGrainTests(GrainClusterFixture fixture) : base(fixture)
    {
        // Reset mutable shared state before each test so that budget store
        // presets, usage event store, options, and the clock do not leak.
        BudgetOptions.AllowNonBudgetedUsers = false;
        BudgetOptions.BudgetCacheTtl = TimeSpan.FromSeconds(1);
        UsageEventStore.Reset();
    }

    private static Money Usd(decimal amount) => new(amount, "USD");

    [Fact]
    public async Task Per_user_override_wins_over_group_budget()
    {
        BudgetStore.SetBudget("override@example.com",
            EffectiveBudget.FromUserOverride(Usd(50m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("override@example.com");
        var result = await grain.CheckBudgetAsync();

        result.Allowed.Should().BeTrue();
        result.EffectiveBudgetAmount.Should().Be(50m);
        result.BudgetSource.Should().Be(BudgetSource.UserOverride);
        result.EffectiveGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Largest_group_budget_is_used_when_no_override()
    {
        var groupId = Guid.NewGuid();
        BudgetStore.SetBudget("group@example.com",
            EffectiveBudget.FromGroup(Usd(200m), groupId));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("group@example.com");
        var result = await grain.CheckBudgetAsync();

        result.Allowed.Should().BeTrue();
        result.EffectiveBudgetAmount.Should().Be(200m);
        result.BudgetSource.Should().Be(BudgetSource.Group);
        result.EffectiveGroupId.Should().Be(groupId);
    }

    [Fact]
    public async Task No_budget_denies_by_default_fail_closed()
    {
        BudgetStore.SetBudget("unbudgeted@example.com", EffectiveBudget.None());

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("unbudgeted@example.com");
        var result = await grain.CheckBudgetAsync();

        result.Allowed.Should().BeFalse("unbudgeted callers are denied by default (fail-closed).");
        result.EffectiveBudgetAmount.Should().BeNull();
        result.BudgetSource.Should().Be(BudgetSource.None);
        result.RemainingAmount.Should().Be(0m);
    }

    [Fact]
    public async Task AllowNonBudgetedUsers_true_allows_unbudgeted_caller()
    {
        BudgetStore.SetBudget("allowed-unbudgeted@example.com", EffectiveBudget.None());
        BudgetOptions.AllowNonBudgetedUsers = true;

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("allowed-unbudgeted@example.com");
        var result = await grain.CheckBudgetAsync();

        result.Allowed.Should().BeTrue("AllowNonBudgetedUsers lets unbudgeted callers through.");
        result.EffectiveBudgetAmount.Should().BeNull();
        result.BudgetSource.Should().Be(BudgetSource.None);
    }

    [Fact]
    public async Task TTL_cache_returns_cached_budget_without_reloading()
    {
        BudgetStore.SetBudget("ttl@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("ttl@example.com");

        await grain.CheckBudgetAsync();
        var callsAfterFirst = BudgetStore.CallCount;

        await grain.CheckBudgetAsync();
        var callsAfterSecond = BudgetStore.CallCount;

        callsAfterSecond.Should().Be(callsAfterFirst,
            "second call within TTL should be served from cache.");
    }

    [Fact]
    public async Task TTL_cache_re_reads_after_expiry()
    {
        BudgetStore.SetBudget("ttl-expiry@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("ttl-expiry@example.com");

        await grain.CheckBudgetAsync();
        var callsAfterFirst = BudgetStore.CallCount;

        // Advance past the TTL (1 second) so the cache is stale.
        TimeProvider.Advance(TimeSpan.FromSeconds(2));

        await grain.CheckBudgetAsync();
        var callsAfterSecond = BudgetStore.CallCount;

        callsAfterSecond.Should().Be(callsAfterFirst + 1,
            "after TTL expiry the grain should re-read from the store.");
    }

    [Fact]
    public async Task Remaining_budget_is_effective_minus_running_spend()
    {
        BudgetStore.SetBudget("remaining@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("remaining@example.com");
        var result = await grain.CheckBudgetAsync();

        result.EffectiveBudgetAmount.Should().Be(100m);
        result.RunningSpendAmount.Should().Be(0m, "no spend accrued yet in M10.");
        result.RemainingAmount.Should().Be(100m);
        result.RemainingCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task Period_rollover_resets_running_spend_and_uses_new_period()
    {
        // Seed a budget so the grain resolves successfully.
        BudgetStore.SetBudget("rollover@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        // First call: January 2026 (the fixture's start time).
        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("rollover@example.com");
        await grain.CheckBudgetAsync();

        BudgetStore.PeriodsCalled.Should().Contain(p => p.Year == 2026 && p.Month == 1,
            "first call should use the current period (Jan 2026).");

        // Advance the clock past the TTL into February 2026.
        TimeProvider.SetUtcNow(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        // Second call: the period should now be February 2026.
        await grain.CheckBudgetAsync();

        BudgetStore.PeriodsCalled.Should().Contain(p => p.Year == 2026 && p.Month == 2,
            "after time advance the grain should resolve the new period (Feb 2026).");
    }

    [Fact]
    public async Task Capture_returns_real_cost_with_pricing()
    {
        BudgetStore.SetBudget("capture-m11@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));
        Store.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m, 1.25m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("capture-m11@example.com");
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
        });

        // Cost = (1000 * 2.5 + 500 * 10) / 1_000_000 = 0.0075
        result.CostAmount.Should().Be(0.0075m);
        result.RunningSpendAmount.Should().Be(0.0075m);
        result.RemainingAmount.Should().Be(100m - 0.0075m);
    }

    [Fact]
    public async Task Cost_computation_correct_for_mixed_cached_and_non_cached_tokens()
    {
        BudgetStore.SetBudget("mixed-cost@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));
        Store.SetPricing("claude-3",
            ModelPricing.Create(Provider.Anthropic, "claude-3",
                TokenPrices.Create(3m, 15m, 0.3m, 3.75m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("mixed-cost@example.com");
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "claude-3",
            TokensInput = 500_000,
            TokensOutput = 200_000,
            TokensCacheRead = 300_000,
            TokensCacheWrite = 100_000,
        });

        // Cost = (500000*3 + 200000*15 + 300000*0.3 + 100000*3.75) / 1M
        //      = (1500000 + 3000000 + 90000 + 375000) / 1M = 4.965
        result.CostAmount.Should().Be(4.965m);
        result.RunningSpendAmount.Should().Be(4.965m);
    }

    [Fact]
    public async Task Capture_appends_audit_row_with_all_required_fields()
    {
        var groupId = Guid.NewGuid();
        BudgetStore.SetBudget("audit@example.com",
            EffectiveBudget.FromGroup(Usd(500m), groupId));
        Store.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m, 1.25m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("audit@example.com");
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            TokensCacheRead = 200,
            TokensCacheWrite = 0,
            RequestId = "req-audit-001",
        });

        UsageEventStore.AppendCallCount.Should().Be(1);
        var evt = UsageEventStore.Events.Single();
        evt.EventId.Should().Be("req-audit-001");
        evt.CallerId.Value.Should().Be("audit@example.com");
        evt.EffectiveGroupId.Should().Be(groupId);
        evt.BudgetSource.Should().Be(BudgetSource.Group);
        evt.Model.Should().Be("gpt-4o");
        evt.TokensInput.Should().Be(1000);
        evt.TokensOutput.Should().Be(500);
        evt.TokensCacheRead.Should().Be(200);
        evt.TokensCacheWrite.Should().Be(0);
        evt.UnitPrices.Input.Should().Be(2.5m);
        evt.UnitPrices.Output.Should().Be(10m);
        evt.UnitPrices.CacheRead.Should().Be(1.25m);
        evt.CostAmount.Should().Be(result.CostAmount);
        evt.CostCurrency.Should().Be("USD");
        evt.RunningSpendAfter.Should().Be(result.RunningSpendAmount);
        evt.Period.Should().Be(BudgetPeriod.FromDate(TimeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task Duplicate_requestId_does_not_double_accrue_or_insert_second_row()
    {
        BudgetStore.SetBudget("idempotent@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));
        Store.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("idempotent@example.com");

        var first = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "req-dup-001",
        });

        var second = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "req-dup-001",
        });

        UsageEventStore.AppendCallCount.Should().Be(1, "second capture returns early via idempotency check, never calling AppendAsync");
        UsageEventStore.Events.Should().HaveCount(1, "only one audit row should exist");
        second.CostAmount.Should().Be(first.CostAmount);
        second.RunningSpendAmount.Should().Be(first.RunningSpendAmount,
            "duplicate capture must not double-accrue.");
    }

    [Fact]
    public async Task Unknown_model_throws_distinct_error()
    {
        BudgetStore.SetBudget("unknown-model@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("unknown-model@example.com");

        var act = async () => await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "nonexistent-model",
            TokensInput = 1000,
            TokensOutput = 500,
        });

        var ex = await act.Should().ThrowAsync<UnknownModelException>();
        ex.Which.Model.Should().Be("nonexistent-model");
    }

    [Fact]
    public async Task Running_spend_reconstructible_by_summing_audit_rows()
    {
        BudgetStore.SetBudget("reconstruct@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));
        Store.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("reconstruct@example.com");

        await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "recon-1",
        });

        await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 2000,
            TokensOutput = 1000,
            RequestId = "recon-2",
        });

        var period = BudgetPeriod.FromDate(TimeProvider.GetUtcNow());
        var events = await UsageEventStore.GetForCallerAsync(
            CallerId.From("reconstruct@example.com"), period);

        events.Should().HaveCount(2);
        var sum = events.Sum(e => e.CostAmount);

        var checkResult = await grain.CheckBudgetAsync();
        sum.Should().Be(checkResult.RunningSpendAmount,
            "running spend should equal the sum of all audit row costs for the period.");
    }

    [Fact]
    public async Task Multiple_captures_accumulate_running_spend()
    {
        BudgetStore.SetBudget("accumulate@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));
        Store.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("accumulate@example.com");

        var first = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "acc-1",
        });

        var second = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "acc-2",
        });

        second.RunningSpendAmount.Should().Be(first.RunningSpendAmount + second.CostAmount,
            "second capture should add to the running spend from the first.");
    }
}
