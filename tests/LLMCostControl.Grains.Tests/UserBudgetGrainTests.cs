using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
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
        // presets, options, and the clock do not leak across tests.
        BudgetOptions.AllowNonBudgetedUsers = false;
        BudgetOptions.BudgetCacheTtl = TimeSpan.FromSeconds(1);
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
    public async Task Capture_returns_zero_cost_in_M10()
    {
        BudgetStore.SetBudget("capture-m10@example.com",
            EffectiveBudget.FromUserOverride(Usd(100m)));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("capture-m10@example.com");
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
        });

        result.CostAmount.Should().Be(0m, "cost accrual is implemented in M11.");
        result.RunningSpendAmount.Should().Be(0m);
        result.RemainingAmount.Should().Be(100m);
    }
}
