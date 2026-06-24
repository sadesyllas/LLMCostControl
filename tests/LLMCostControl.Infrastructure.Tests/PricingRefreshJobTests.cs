using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMCostControl.Infrastructure.Tests;

public class PricingRefreshJobTests : RepositoryTestBase
{
    private static ModelPricing MakePricing(Provider provider, string model, decimal input = 2.5m)
        => ModelPricing.Create(provider, model, TokenPrices.Create(input, 10m, 1.25m));

    [Fact]
    public async Task RefreshAll_invokes_each_adapter_and_persists_results()
    {
        var openAi = new StubPricingAdapter(Provider.OpenAI, [
            MakePricing(Provider.OpenAI, "gpt-4o"),
            MakePricing(Provider.OpenAI, "gpt-4o-mini", 0.15m),
        ]);
        var anthropic = new StubPricingAdapter(Provider.Anthropic, [
            MakePricing(Provider.Anthropic, "claude-3-5-sonnet", 3m),
        ]);

        var repo = new ModelPricingRepository(Db);
        var publisher = new StubPricingUpdatePublisher();
        var job = new PricingRefreshJob(
            [openAi, anthropic],
            repo,
            publisher,
            new PricingRefreshOptions(),
            NullLogger<PricingRefreshJob>.Instance);

        await job.RefreshAllAsync();

        openAi.CallCount.Should().Be(1);
        anthropic.CallCount.Should().Be(1);

        var openAiEntries = await repo.GetByProviderAsync(Provider.OpenAI);
        openAiEntries.Should().HaveCount(2);
        openAiEntries.Select(e => e.Model).Should().BeEquivalentTo(["gpt-4o", "gpt-4o-mini"]);

        var anthropicEntries = await repo.GetByProviderAsync(Provider.Anthropic);
        anthropicEntries.Should().HaveCount(1);
        anthropicEntries[0].Model.Should().Be("claude-3-5-sonnet");
    }

    [Fact]
    public async Task RefreshAll_publishes_events_with_affected_model_names()
    {
        var adapter = new StubPricingAdapter(Provider.OpenAI, [
            MakePricing(Provider.OpenAI, "gpt-4o"),
            MakePricing(Provider.OpenAI, "gpt-4o-mini", 0.15m),
        ]);

        var repo = new ModelPricingRepository(Db);
        var publisher = new StubPricingUpdatePublisher();
        var job = new PricingRefreshJob(
            [adapter],
            repo,
            publisher,
            new PricingRefreshOptions(),
            NullLogger<PricingRefreshJob>.Instance);

        await job.RefreshAllAsync();

        publisher.Published.Should().HaveCount(1);
        var (provider, models) = publisher.Published[0];
        provider.Should().Be(Provider.OpenAI);
        models.Should().BeEquivalentTo(["gpt-4o", "gpt-4o-mini"]);
    }

    [Fact]
    public async Task Singularity_guard_prevents_concurrent_execution()
    {
        var tcs = new TaskCompletionSource();
        var slowAdapter = new SlowPricingAdapter(Provider.OpenAI, tcs);

        var repo = new ModelPricingRepository(Db);
        var publisher = new StubPricingUpdatePublisher();
        var job = new PricingRefreshJob(
            [slowAdapter],
            repo,
            publisher,
            new PricingRefreshOptions(),
            NullLogger<PricingRefreshJob>.Instance);

        var firstRun = Task.Run(() => job.RefreshAllAsync());
        await Task.Delay(50);
        var secondRun = Task.Run(() => job.RefreshAllAsync());
        await secondRun;

        slowAdapter.CallCount.Should().Be(1);
        tcs.SetResult();
        await firstRun;
    }

    [Fact]
    public async Task RefreshDueAdapters_skips_adapters_whose_cadence_has_not_elapsed()
    {
        var adapter = new StubPricingAdapter(Provider.OpenAI, [MakePricing(Provider.OpenAI, "gpt-4o")]);

        var repo = new ModelPricingRepository(Db);
        var publisher = new StubPricingUpdatePublisher();
        var options = new PricingRefreshOptions
        {
            DefaultCadence = TimeSpan.FromHours(1),
            DefaultJitter = TimeSpan.Zero,
        };
        var job = new PricingRefreshJob(
            [adapter],
            repo,
            publisher,
            options,
            NullLogger<PricingRefreshJob>.Instance);

        await job.RefreshDueAdaptersAsync();
        adapter.CallCount.Should().Be(1);

        await job.RefreshDueAdaptersAsync();
        adapter.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshDueAdapters_refreshes_when_cadence_has_elapsed()
    {
        var adapter = new StubPricingAdapter(Provider.OpenAI, [MakePricing(Provider.OpenAI, "gpt-4o")]);

        var repo = new ModelPricingRepository(Db);
        var publisher = new StubPricingUpdatePublisher();
        var options = new PricingRefreshOptions
        {
            DefaultCadence = TimeSpan.FromMilliseconds(1),
            DefaultJitter = TimeSpan.Zero,
        };
        var job = new PricingRefreshJob(
            [adapter],
            repo,
            publisher,
            options,
            NullLogger<PricingRefreshJob>.Instance);

        await job.RefreshDueAdaptersAsync();
        adapter.CallCount.Should().Be(1);

        await Task.Delay(20);
        await job.RefreshDueAdaptersAsync();
        adapter.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task RefreshAll_replaces_old_entries_for_provider()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(MakePricing(Provider.OpenAI, "gpt-4o", 2.5m));
        await repo.UpsertAsync(MakePricing(Provider.OpenAI, "old-model", 1m));

        var adapter = new StubPricingAdapter(Provider.OpenAI, [
            MakePricing(Provider.OpenAI, "gpt-4o", 3m),
            MakePricing(Provider.OpenAI, "gpt-4o-mini", 0.15m),
        ]);

        var publisher = new StubPricingUpdatePublisher();
        var job = new PricingRefreshJob(
            [adapter],
            repo,
            publisher,
            new PricingRefreshOptions(),
            NullLogger<PricingRefreshJob>.Instance);

        await job.RefreshAllAsync();

        var entries = await repo.GetByProviderAsync(Provider.OpenAI);
        entries.Should().HaveCount(2);
        entries.Select(e => e.Model).Should().BeEquivalentTo(["gpt-4o", "gpt-4o-mini"]);
        entries.Single(e => e.Model == "gpt-4o").Prices.Input.Should().Be(3m);
        (await repo.GetByModelAsync("old-model")).Should().BeNull();
    }

    private sealed class SlowPricingAdapter(Provider provider, TaskCompletionSource tcs) : IPricingAdapter
    {
        private int _callCount;

        public Provider Provider => provider;
        public int CallCount => _callCount;

        public async Task<IReadOnlyCollection<ModelPricing>> FetchAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            await tcs.Task;
            return [MakePricing(provider, "test-model")];
        }
    }
}
