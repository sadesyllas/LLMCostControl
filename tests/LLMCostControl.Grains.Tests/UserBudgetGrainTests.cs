using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Implementations;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.State;
using LLMCostControl.Grains.Storage;
using LLMostControl.Grains.Tests;
using NSubstitute;
using Orleans;
using Orleans.Runtime;

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

    [Fact]
    public async Task CaptureUsageAsync_deactivates_grain_on_write_state_failure()
    {
        // Arrange
        var budgetStore = NSubstitute.Substitute.For<IBudgetStore>();
        var usageEventStore = NSubstitute.Substitute.For<IUsageEventStore>();
        var storage = NSubstitute.Substitute.For<IPersistentState<UserBudgetGrainState>>();
        var grainContext = NSubstitute.Substitute.For<IGrainContext>();
        var grainRuntime = NSubstitute.Substitute.For<IGrainRuntime>();
        var timeProvider = System.TimeProvider.System;
        
        var options = new BudgetGrainOptions
        {
            BudgetCacheTtl = TimeSpan.FromSeconds(1),
            AllowNonBudgetedUsers = false
        };

        var currentPeriod = BudgetPeriod.FromDate(timeProvider.GetUtcNow());
        var state = new UserBudgetGrainState
        {
            PeriodYear = currentPeriod.Year,
            PeriodMonth = currentPeriod.Month,
            RunningSpendCurrency = "USD"
        };
        storage.State.Returns(state);
        storage.WriteStateAsync().Returns(x => Task.FromException(new InvalidOperationException("DB error")));

        var grain = new UserBudgetGrain(
            budgetStore,
            usageEventStore,
            options,
            timeProvider,
            storage);

        // Inject the mocked grain context and grain runtime via reflection so DeactivateOnIdle() doesn't throw NullReferenceException
        var grainType = typeof(Grain);
        var contextField = grainType.GetField("<GrainContext>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        contextField?.SetValue(grain, grainContext);
        var runtimeField = grainType.GetField("<Runtime>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        runtimeField?.SetValue(grain, grainRuntime);

        // Mock the GrainFactory lookups via the ServiceProvider on GrainContext
        var pricingGrain = NSubstitute.Substitute.For<IPricingGrain>();
        pricingGrain.GetPricingAsync().Returns(new PricingResult
        {
            Input = 2.5m,
            Output = 10m,
            Currency = "USD"
        });

        var grainFactory = NSubstitute.Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<IPricingGrain>("gpt-4", Arg.Any<string?>()).Returns(pricingGrain);

        var serviceProvider = NSubstitute.Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IGrainFactory)).Returns(grainFactory);
        serviceProvider.GetService(typeof(IGrainRuntime)).Returns(grainRuntime);

        grainRuntime.GrainFactory.Returns(grainFactory);

        grainContext.ActivationServices.Returns(serviceProvider);

        // Mock GetPrimaryKeyString via GrainContext.GrainId.Key
        var grainId = Orleans.Runtime.GrainId.Parse("UserBudgetGrain/test-user@example.com");
        grainContext.GrainId.Returns(grainId);

        // Mock budgetStore.ResolveAsync to return a valid override
        budgetStore.ResolveAsync(Arg.Any<CallerId>(), Arg.Any<BudgetPeriod>())
            .Returns(EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var request = new UsageCaptureRequest
        {
            Model = "gpt-4",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "test-req-fail"
        };

        // Act
        Func<Task> action = () => grain.CaptureUsageAsync(request);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
        grainRuntime.Received(1).DeactivateOnIdle(grainContext);
    }

    [Fact]
    public void TestDeactivateOnIdleDirectly()
    {
        var budgetStore = NSubstitute.Substitute.For<IBudgetStore>();
        var usageEventStore = NSubstitute.Substitute.For<IUsageEventStore>();
        var storage = NSubstitute.Substitute.For<IPersistentState<UserBudgetGrainState>>();
        var grainContext = NSubstitute.Substitute.For<IGrainContext>();
        var grainRuntime = NSubstitute.Substitute.For<IGrainRuntime>();
        var timeProvider = System.TimeProvider.System;
        
        var options = new BudgetGrainOptions();
        var grain = new UserBudgetGrain(
            budgetStore,
            usageEventStore,
            options,
            timeProvider,
            storage);

        var grainType = typeof(Grain);
        var contextField = grainType.GetField("<GrainContext>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        contextField?.SetValue(grain, grainContext);
        var runtimeField = grainType.GetField("<Runtime>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        runtimeField?.SetValue(grain, grainRuntime);

        // Let's print properties via reflection to see if they are set correctly
        var actualContext = grain.GrainContext;
        var actualRuntime = grainType.GetProperty("Runtime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(grain);
        
        if (actualRuntime != grainRuntime)
        {
            throw new Exception($"Runtime not equal! Expected: {grainRuntime}, got: {actualRuntime}");
        }

        // Call DeactivateOnIdle
        var method = grainType.GetMethod("DeactivateOnIdle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(grain, null);

        grainRuntime.Received(1).DeactivateOnIdle(grainContext);
    }

    [Fact]
    public async Task Capture_usage_throws_when_currency_mismatches_budget_currency()
    {
        BudgetStore.SetBudget("mismatch-budget@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        Store.SetPricing("model-eur",
            ModelPricing.Create(Provider.OpenAI, "model-eur",
                TokenPrices.Create(2.5m, 10m), "EUR"));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("mismatch-budget@example.com");
        
        Func<Task> act = () => grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "model-eur",
            TokensInput = 1000,
            TokensOutput = 500,
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match budget currency*");
    }

    [Fact]
    public async Task Capture_usage_throws_when_currency_mismatches_existing_running_spend_currency()
    {
        BudgetStore.SetBudget("mismatch-spend@example.com", EffectiveBudget.None());
        
        Store.SetPricing("model-eur-2",
            ModelPricing.Create(Provider.OpenAI, "model-eur-2",
                TokenPrices.Create(2.5m, 10m), "EUR"));
        Store.SetPricing("model-usd-2",
            ModelPricing.Create(Provider.OpenAI, "model-usd-2",
                TokenPrices.Create(2.5m, 10m), "USD"));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("mismatch-spend@example.com");

        // First capture in EUR succeeds
        await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "model-eur-2",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "req-eur"
        });

        // Second capture in USD throws
        Func<Task> act = () => grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "model-usd-2",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "req-usd"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public async Task Check_budget_throws_when_currency_mismatches_existing_running_spend_currency()
    {
        // 1. Start with no budget
        BudgetStore.SetBudget("mismatch-check@example.com", EffectiveBudget.None());
        
        Store.SetPricing("model-eur-3",
            ModelPricing.Create(Provider.OpenAI, "model-eur-3",
                TokenPrices.Create(2.5m, 10m), "EUR"));

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("mismatch-check@example.com");

        // 2. Capture in EUR
        await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "model-eur-3",
            TokensInput = 1000,
            TokensOutput = 500,
            RequestId = "req-check-eur"
        });

        // 3. Now assign a budget in USD
        BudgetStore.SetBudget("mismatch-check@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        // Advance time to expire the budget cache TTL (default 1s in tests)
        TimeProvider.Advance(TimeSpan.FromSeconds(2));

        // 4. Checking budget should throw due to currency mismatch
        Func<Task> act = () => grain.CheckBudgetAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public async Task Period_rollover_invalidates_cache_even_before_ttl_expires()
    {
        // Seed budgets for Jan and Feb
        var janBudget = EffectiveBudget.FromUserOverride(new Money(100m, "USD"));
        var febBudget = EffectiveBudget.FromUserOverride(new Money(50m, "USD"));
        
        BudgetStore.SetBudget("cache-rollover@example.com", janBudget);

        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("cache-rollover@example.com");
        
        // 1. Initial call in January 2026
        TimeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 31, 23, 59, 50, TimeSpan.Zero));
        var resJan = await grain.CheckBudgetAsync();
        resJan.EffectiveBudgetAmount.Should().Be(100m);

        // Update the mock store with the new budget for February
        BudgetStore.SetBudget("cache-rollover@example.com", febBudget);

        // 2. Advance time by 15 seconds (less than 30s TTL cache, but rolls over to February)
        TimeProvider.SetUtcNow(new DateTimeOffset(2026, 2, 1, 0, 0, 5, TimeSpan.Zero));
        
        // This call should bypass cache because period changed, resolving the new Feb budget ($50)
        var resFeb = await grain.CheckBudgetAsync();
        resFeb.EffectiveBudgetAmount.Should().Be(50m);
    }
}
