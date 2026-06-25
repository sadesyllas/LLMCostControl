using LLMCostControl.Grains.Abstractions;
using LLMostControl.Grains.Tests;
using Orleans.Streams;

namespace LLMCostControl.Grains.Tests;

public class SiloBootstrapTests : GrainTestBase
{
    [Fact]
    public async Task Silo_boots_and_grain_round_trip_succeeds()
    {
        var grain = GrainFactory.GetGrain<IUserBudgetGrain>("test@example.com");

        var result = await grain.CheckBudgetAsync();

        result.Should().NotBeNull();
        result.Allowed.Should().BeTrue();
        result.CallerId.Should().Be("test@example.com");
    }

    [Fact]
    public async Task PricingGrain_returns_pricing()
    {
        var grain = GrainFactory.GetGrain<IPricingGrain>("gpt-4o");

        var result = await grain.GetPricingAsync();

        result.Should().NotBeNull();
        result!.Input.Should().BePositive();
        result.Output.Should().BePositive();
    }

    [Fact]
    public async Task UserBudgetGrain_capture_returns_result()
    {
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
        result.CostAmount.Should().BePositive();
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
