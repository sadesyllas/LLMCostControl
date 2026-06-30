using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Tests;

public class ModelPricingRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task Upsert_then_GetByModel_returns_pricing()
    {
        var pricing = ModelPricing.Create(
            Provider.OpenAI,
            "gpt-4o",
            TokenPrices.Create(2.5m, 10m, 1.25m));

        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(pricing);

        var fetched = await repo.GetByModelAsync("gpt-4o");
        fetched.Should().NotBeNull();
        fetched!.Provider.Should().Be(Provider.OpenAI);
        fetched.Prices.Input.Should().Be(2.5m);
        fetched.Prices.Output.Should().Be(10m);
        fetched.Prices.CacheRead.Should().Be(1.25m);
    }

    [Fact]
    public async Task Upsert_replaces_existing_pricing_for_same_model()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));

        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(5m, 15m)));

        var all = await repo.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].Prices.Input.Should().Be(5m);
        all[0].Prices.Output.Should().Be(15m);
    }

    [Fact]
    public async Task ReplaceProviderPricing_removes_old_and_inserts_new()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o-mini", TokenPrices.Create(0.15m, 0.6m)));

        var newEntries = new[]
        {
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(3m, 12m)),
            ModelPricing.Create(Provider.OpenAI, "o1", TokenPrices.Create(15m, 60m)),
        };
        await repo.ReplaceProviderPricingAsync(Provider.OpenAI, newEntries);

        var openai = await repo.GetByProviderAsync(Provider.OpenAI);
        openai.Should().HaveCount(2);
        openai.Select(p => p.Model).Should().BeEquivalentTo(["gpt-4o", "o1"]);
        (await repo.GetByModelAsync("gpt-4o-mini")).Should().BeNull();
    }

    [Fact]
    public async Task Allows_duplicate_model_names_across_different_providers()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.Google, "gpt-4o", TokenPrices.Create(3.0m, 12m)));

        var all = await repo.GetAllAsync();
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rejects_duplicate_model_names_for_same_provider_via_database_constraint()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));

        // Bypass UpsertAsync and try to add duplicate directly to DB to trigger constraint
        await Db.ModelPricing.AddAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(3.0m, 12m)));

        Func<Task> act = () => Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ReplaceMultipleProvidersPricing_atomically_replaces_all_specified_providers()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.Google, "gemini-1.5-pro", TokenPrices.Create(7m, 21m)));
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.Anthropic, "claude-3-opus", TokenPrices.Create(15m, 75m)));

        var newEntries = new[]
        {
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(3m, 12m)),
            ModelPricing.Create(Provider.OpenAI, "o1", TokenPrices.Create(15m, 60m)),
            ModelPricing.Create(Provider.Google, "gemini-1.5-flash", TokenPrices.Create(0.075m, 0.3m))
        };

        await repo.ReplaceMultipleProvidersPricingAsync(newEntries);

        // OpenAI should have new entries and delete old
        var openai = await repo.GetByProviderAsync(Provider.OpenAI);
        openai.Select(p => p.Model).Should().BeEquivalentTo(["gpt-4o", "o1"]);
        openai.First(p => p.Model == "gpt-4o").Prices.Input.Should().Be(3m);

        // Google should have new entries and delete old
        var google = await repo.GetByProviderAsync(Provider.Google);
        google.Select(p => p.Model).Should().BeEquivalentTo(["gemini-1.5-flash"]);

        // Anthropic should be untouched because it wasn't specified in newEntries
        var anthropic = await repo.GetByProviderAsync(Provider.Anthropic);
        anthropic.Select(p => p.Model).Should().BeEquivalentTo(["claude-3-opus"]);
    }

    [Fact]
    public async Task ReplaceMultipleProvidersPricing_rolls_back_completely_on_failure()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.UpsertAsync(ModelPricing.Create(
            Provider.Google, "gemini-1.5-pro", TokenPrices.Create(7m, 21m)));

        // This list contains a duplicate key for OpenAI which triggers a constraint failure
        var invalidEntries = new[]
        {
            ModelPricing.Create(Provider.OpenAI, "gpt-4o-new", TokenPrices.Create(3m, 12m)),
            ModelPricing.Create(Provider.Google, "gemini-1.5-flash", TokenPrices.Create(0.075m, 0.3m)),
            // Duplicate model for OpenAI:
            ModelPricing.Create(Provider.OpenAI, "gpt-4o-new", TokenPrices.Create(5m, 15m))
        };

        Func<Task> act = () => repo.ReplaceMultipleProvidersPricingAsync(invalidEntries);
        await act.Should().ThrowAsync<DbUpdateException>();

        // Verify rollback: OpenAI and Google pricing must remain unchanged
        var openai = await repo.GetByProviderAsync(Provider.OpenAI);
        openai.Select(p => p.Model).Should().BeEquivalentTo(["gpt-4o"]);
        openai.First(p => p.Model == "gpt-4o").Prices.Input.Should().Be(2.5m);

        var google = await repo.GetByProviderAsync(Provider.Google);
        google.Select(p => p.Model).Should().BeEquivalentTo(["gemini-1.5-pro"]);
    }
}

