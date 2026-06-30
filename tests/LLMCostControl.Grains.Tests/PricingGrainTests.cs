using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;

namespace LLMCostControl.Grains.Tests;

public class PricingGrainTests : GrainTestBase
{
    public PricingGrainTests(GrainClusterFixture fixture) : base(fixture) { }

    private static ModelPricing MakePricing(string model, decimal input, decimal output, decimal? cacheRead = null)
        => ModelPricing.Create(Provider.OpenAI, model, TokenPrices.Create(input, output, cacheRead));

    [Fact]
    public async Task Grain_loads_pricing_from_store_on_first_access()
    {
        Store.SetPricing(MakePricing("gpt-4o", 2.5m, 10m, 1.25m));

        var grain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(Provider.OpenAI, "gpt-4o"));

        var result = await grain.GetPricingAsync();

        result.Should().NotBeNull();
        result!.Input.Should().Be(2.5m);
        result.Output.Should().Be(10m);
        result.CacheRead.Should().Be(1.25m);
        result.Currency.Should().Be("USD");
        result.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Grain_returns_null_for_unknown_model()
    {
        var grain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(Provider.OpenAI, "totally-unknown-model"));

        var result = await grain.GetPricingAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task Cached_pricing_is_returned_without_reloading_from_store()
    {
        Store.SetPricing(MakePricing("cache-test", 2.5m, 10m));
        var grain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(Provider.OpenAI, "cache-test"));

        await grain.GetPricingAsync();
        var callsAfterFirst = Store.CallCount;

        await grain.GetPricingAsync();
        var callsAfterSecond = Store.CallCount;

        callsAfterSecond.Should().Be(callsAfterFirst,
            "second call should be served from cache without hitting the store.");
    }

    [Fact]
    public async Task StatelessWorker_provides_local_activations_with_multiple_instances()
    {
        Store.SetPricing(MakePricing("concurrent-test", 2.5m, 10m));

        var grain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(Provider.OpenAI, "concurrent-test"));

        var firstResult = await grain.GetPricingAsync();
        firstResult.Should().NotBeNull();
        firstResult!.Input.Should().Be(2.5m);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => grain.GetPricingAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r!.Input.Should().Be(2.5m);
        });
    }

    [Fact]
    public async Task Stale_pricing_is_reflected_in_result()
    {
        var stalePricing = ModelPricing.Create(
            Provider.OpenAI, "stale-model", TokenPrices.Create(2.5m, 10m, 1.25m));
        stalePricing.StaleSince = DateTimeOffset.UtcNow;
        Store.SetPricing(stalePricing);

        var grain = GrainFactory.GetGrain<IPricingGrain>(ProviderResolver.Key(Provider.OpenAI, "stale-model"));

        var result = await grain.GetPricingAsync();

        result.Should().NotBeNull();
        result!.IsStale.Should().BeTrue();
    }
}
