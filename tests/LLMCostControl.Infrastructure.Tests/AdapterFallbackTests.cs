using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Tests;

public class AdapterFallbackTests : RepositoryTestBase
{
    [Fact]
    public async Task OpenAI_adapter_falls_back_to_persisted_values_on_fetch_failure()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        var seeded = ModelPricing.Create(
            Provider.OpenAI,
            "gpt-4o",
            TokenPrices.Create(2.5m, 10m, 1.25m));
        await pricingRepo.UpsertAsync(seeded);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new OpenAIPricingAdapter(client, "http://test/openai", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var entry = results.Single();
        entry.Model.Should().Be("gpt-4o");
        entry.IsStale.Should().BeTrue();
        entry.StaleSince.Should().NotBeNull();
        entry.Prices.Input.Should().Be(2.5m);
    }

    [Fact]
    public async Task Anthropic_adapter_falls_back_to_persisted_values_on_fetch_failure()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        var seeded = ModelPricing.Create(
            Provider.Anthropic,
            "claude-3-5-sonnet",
            TokenPrices.Create(3m, 15m, 0.3m, 3.75m));
        await pricingRepo.UpsertAsync(seeded);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new AnthropicPricingAdapter(client, "http://test/anthropic", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var entry = results.Single();
        entry.Model.Should().Be("claude-3-5-sonnet");
        entry.IsStale.Should().BeTrue();
        entry.StaleSince.Should().NotBeNull();
        entry.Prices.CacheWrite.Should().Be(3.75m);
    }

    [Fact]
    public async Task Google_adapter_falls_back_to_persisted_values_on_fetch_failure()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        var seeded = ModelPricing.Create(
            Provider.Google,
            "gemini-1.5-pro",
            TokenPrices.Create(1.25m, 5m, null, null));
        await pricingRepo.UpsertAsync(seeded);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new GooglePricingAdapter(client, "http://test/google", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var entry = results.Single();
        entry.Model.Should().Be("gemini-1.5-pro");
        entry.IsStale.Should().BeTrue();
        entry.StaleSince.Should().NotBeNull();
        entry.Prices.Input.Should().Be(1.25m);
    }

    [Fact]
    public async Task AzureFoundry_adapter_falls_back_to_persisted_values_on_fetch_failure()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        var seeded = ModelPricing.Create(
            Provider.AzureFoundry,
            "gpt-4o",
            TokenPrices.Create(2.5m, 10m, 1.25m));
        await pricingRepo.UpsertAsync(seeded);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new AzureFoundryPricingAdapter(client, "http://test/azure", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var entry = results.Single();
        entry.Model.Should().Be("gpt-4o");
        entry.IsStale.Should().BeTrue();
        entry.StaleSince.Should().NotBeNull();
        entry.Prices.Input.Should().Be(2.5m);
    }

    [Fact]
    public async Task VertexAI_adapter_falls_back_to_persisted_values_on_fetch_failure()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        var seeded = ModelPricing.Create(
            Provider.VertexAI,
            "gemini-1.5-pro",
            TokenPrices.Create(1.25m, 5m, null, null));
        await pricingRepo.UpsertAsync(seeded);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new VertexAIPricingAdapter(client, "http://test/vertex", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var entry = results.Single();
        entry.Model.Should().Be("gemini-1.5-pro");
        entry.IsStale.Should().BeTrue();
        entry.StaleSince.Should().NotBeNull();
        entry.Prices.Input.Should().Be(1.25m);
    }

    [Fact]
    public async Task Fallback_returns_empty_when_no_persisted_values_exist()
    {
        var pricingRepo = new ModelPricingRepository(Db);

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new OpenAIPricingAdapter(client, "http://test/openai", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Fallback_sets_staleSince_on_all_returned_entries()
    {
        var pricingRepo = new ModelPricingRepository(Db);
        await pricingRepo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await pricingRepo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o-mini", TokenPrices.Create(0.15m, 0.6m)));

        using var handler = StubHttpMessageHandler.Throwing();
        using var client = new HttpClient(handler);
        var adapter = new OpenAIPricingAdapter(client, "http://test/openai", pricingRepo);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.IsStale.Should().BeTrue());
        results.Should().AllSatisfy(e => e.StaleSince.Should().NotBeNull());
    }
}
