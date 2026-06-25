using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMostControl.Grains.Tests;
using Orleans.Streams;

namespace LLMCostControl.Grains.Tests;

public class SiloBootstrapTests : GrainTestBase
{
    public SiloBootstrapTests(GrainClusterFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Silo_boots_and_grain_round_trip_succeeds()
    {
        BudgetStore.SetBudget("test@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("test@example.com");

        var result = await grain.CheckBudgetAsync();

        result.Should().NotBeNull();
        result.Allowed.Should().BeTrue();
        result.CallerId.Should().Be("test@example.com");
    }

    [Fact]
    public async Task PricingGrain_returns_pricing()
    {
        Store.SetPricing("gpt-4o", ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));
        var grain = GrainFactory.GetGrain<IPricingGrain>("gpt-4o");

        var result = await grain.GetPricingAsync();

        result.Should().NotBeNull();
        result!.Input.Should().BePositive();
        result.Output.Should().BePositive();
    }

    [Fact]
    public async Task UserBudgetGrain_capture_returns_result()
    {
        BudgetStore.SetBudget("capture@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        Store.SetPricing("gpt-4o", ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));
        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("capture@example.com");

        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = "gpt-4o",
            TokensInput = 1000,
            TokensOutput = 500,
            TokensCacheRead = 200,
            TokensCacheWrite = 0,
        });

        result.Should().NotBeNull();
        result.CallerId.Should().Be("capture@example.com");
        result.CostAmount.Should().BePositive("pricing is set and tokens are non-zero.");
    }

    [Fact]
    public async Task Stream_provider_can_publish_and_subscribe()
    {
        var streamProvider = Client.GetStreamProvider("pricing");
        var stream = streamProvider.GetStream<string>("pricing", "test-stream");

        var received = new TaskCompletionSource<string>();
        var observer = new SimpleAsyncObserver<string>(msg => received.TrySetResult(msg));

        await stream.SubscribeAsync(observer);

        await stream.OnNextAsync("hello");

        var msg = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        msg.Should().Be("hello");
    }
}
