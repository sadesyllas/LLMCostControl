using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LLMCostControl.Infrastructure.Tests;

public class ModelPricingRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task InsertNewVersion_then_GetByModel_returns_latest_pricing()
    {
        var repo = new ModelPricingRepository(Db);
        var pricing = ModelPricing.Create(
            Provider.OpenAI,
            "gpt-4o",
            TokenPrices.Create(2.5m, 10m, 1.25m));

        var id = await repo.InsertNewVersionAsync(pricing);

        var fetched = await repo.GetByModelAsync("gpt-4o");
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(id);
        fetched.Provider.Should().Be(Provider.OpenAI);
        fetched.Prices.Input.Should().Be(2.5m);
        fetched.Prices.Output.Should().Be(10m);
        fetched.Prices.CacheRead.Should().Be(1.25m);
    }

    [Fact]
    public async Task InsertNewVersion_identical_pricing_is_noop()
    {
        var repo = new ModelPricingRepository(Db);

        var v1 = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m));
        var id1 = await repo.InsertNewVersionAsync(v1);

        var v2 = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m));
        var id2 = await repo.InsertNewVersionAsync(v2);

        id1.Should().Be(id2, "identical pricing should return the same version GUID");

        var count = await Db.ModelPricing.CountAsync(p => p.Provider == Provider.OpenAI && p.Model == "gpt-4o");
        count.Should().Be(1, "no new row should be inserted for identical pricing");
    }

    [Fact]
    public async Task InsertNewVersion_different_pricing_inserts_new_version()
    {
        var repo = new ModelPricingRepository(Db);

        var v1 = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m));
        var id1 = await repo.InsertNewVersionAsync(v1);

        var v2 = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(3.0m, 12m));
        var id2 = await repo.InsertNewVersionAsync(v2);

        id1.Should().NotBe(id2, "different pricing should write a new version with a new GUID");

        var count = await Db.ModelPricing.CountAsync(p => p.Provider == Provider.OpenAI && p.Model == "gpt-4o");
        count.Should().Be(2, "both version rows must exist in the database");

        var latest = await repo.GetByProviderAndModelAsync(Provider.OpenAI, "gpt-4o");
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(id2);
        latest.Prices.Input.Should().Be(3.0m);
    }

    [Fact]
    public async Task ReplaceProviderPricing_retains_history_and_adds_new_versions()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.InsertNewVersionAsync(ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.InsertNewVersionAsync(ModelPricing.Create(Provider.OpenAI, "gpt-4o-mini", TokenPrices.Create(0.15m, 0.6m)));

        var newEntries = new[]
        {
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(3m, 12m)),
            ModelPricing.Create(Provider.OpenAI, "o1", TokenPrices.Create(15m, 60m)),
        };
        await repo.ReplaceProviderPricingAsync(Provider.OpenAI, newEntries);

        // Active models for OpenAI should be gpt-4o, gpt-4o-mini (retained because no new version, so last is active), and o1
        var openai = await repo.GetByProviderAsync(Provider.OpenAI);
        openai.Should().HaveCount(3);
        openai.Select(p => p.Model).Should().BeEquivalentTo(["gpt-4o", "gpt-4o-mini", "o1"]);

        var gpt4o = openai.Single(p => p.Model == "gpt-4o");
        gpt4o.Prices.Input.Should().Be(3m, "new version is the active version");

        var totalRows = await Db.ModelPricing.CountAsync(p => p.Provider == Provider.OpenAI);
        totalRows.Should().Be(4, "2 original rows + 2 new version rows inserted (gpt-4o updated, o1 new, mini unchanged but retained)");
    }

    [Fact]
    public async Task ReplaceMultipleProvidersPricing_performs_insert_on_change_for_multiple_providers()
    {
        var repo = new ModelPricingRepository(Db);
        await repo.InsertNewVersionAsync(ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));
        await repo.InsertNewVersionAsync(ModelPricing.Create(Provider.Google, "gemini-1.5-pro", TokenPrices.Create(7m, 21m)));
        await repo.InsertNewVersionAsync(ModelPricing.Create(Provider.Anthropic, "claude-3-opus", TokenPrices.Create(15m, 75m)));

        var newEntries = new[]
        {
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(3m, 12m)),
            ModelPricing.Create(Provider.OpenAI, "o1", TokenPrices.Create(15m, 60m)),
            ModelPricing.Create(Provider.Google, "gemini-1.5-flash", TokenPrices.Create(0.075m, 0.3m))
        };

        await repo.ReplaceMultipleProvidersPricingAsync(newEntries);

        // OpenAI has gpt-4o (new version) and o1
        var openai = await repo.GetByProviderAsync(Provider.OpenAI);
        openai.Select(p => p.Model).Should().BeEquivalentTo(["gpt-4o", "o1"]);
        openai.Single(p => p.Model == "gpt-4o").Prices.Input.Should().Be(3m);

        // Google has gemini-1.5-flash and gemini-1.5-pro (retained)
        var google = await repo.GetByProviderAsync(Provider.Google);
        google.Select(p => p.Model).Should().BeEquivalentTo(["gemini-1.5-flash", "gemini-1.5-pro"]);

        // Anthropic should be untouched
        var anthropic = await repo.GetByProviderAsync(Provider.Anthropic);
        anthropic.Select(p => p.Model).Should().BeEquivalentTo(["claude-3-opus"]);
    }

    [Fact]
    public async Task staleSince_is_computed_dynamically_and_not_persisted()
    {
        var repo = new ModelPricingRepository(Db);
        var pricing = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m));
        var id = await repo.InsertNewVersionAsync(pricing);

        // Retrieve from repository
        var retrieved = await repo.GetByProviderAndModelAsync(Provider.OpenAI, "gpt-4o");
        retrieved.Should().NotBeNull();
        retrieved!.StaleSince.Should().BeNull("freshly loaded entry has no staleSince set");

        // Manually set dynamic staleness on read path (mimicking the adapter)
        retrieved.StaleSince = DateTimeOffset.UtcNow.AddMinutes(-5);
        retrieved.IsStale.Should().BeTrue();

        // Save changes to database (or check database context)
        Db.Entry(retrieved).State = EntityState.Modified;
        await Db.SaveChangesAsync();

        // Clear tracker state and reload to verify stale_since was NOT written to DB
        Db.ChangeTracker.Clear();
        var reloaded = await repo.GetByProviderAndModelAsync(Provider.OpenAI, "gpt-4o");
        reloaded.Should().NotBeNull();
        reloaded!.StaleSince.Should().BeNull("database does not store staleSince");
    }
}
