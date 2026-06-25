using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Integration tests (Testcontainers Postgres) for the M18 read-only views and
/// pricing-file upload (§12.3): effective-budget resolution through the shared
/// path and the real pricing import write path. Requires Docker.
/// </summary>
public sealed class AdminQueryAndPricingIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private TestDbContextFactory _factory = null!;
    private AdminCommandService _commands = null!;
    private AdminQueryService _queries = null!;
    private PricingImportService _import = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var db = new CostTrackerDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _factory = new TestDbContextFactory(options);
        _commands = new AdminCommandService(_factory);
        _queries = new AdminQueryService(_factory);
        _import = new PricingImportService(
            new DbPricingWriter(_factory),
            new NoOpPricingUpdatePublisher(),
            NullLogger<PricingImportService>.Instance);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Caller_in_two_groups_resolves_to_the_largest_budget_with_spend_and_remaining()
    {
        var small = await _commands.CreateGroupAsync("Small");
        var large = await _commands.CreateGroupAsync("Large");
        await _commands.SetGroupBudgetAsync(small.Id, 100m, "USD");
        await _commands.SetGroupBudgetAsync(large.Id, 250m, "USD");

        const string caller = "multi@example.com";
        await _commands.AddMemberAsync(small.Id, caller);
        await _commands.AddMemberAsync(large.Id, caller);

        await AppendUsageAsync(caller, large.Id, 10m);

        var summary = await _queries.GetCallerSummaryAsync(caller);

        summary.EffectiveBudget.Source.Should().Be(BudgetSource.Group);
        summary.EffectiveBudget.Amount!.Amount.Should().Be(250m, "the largest group budget wins");
        summary.RunningSpend.Amount.Should().Be(10m);
        summary.Remaining!.Amount.Should().Be(240m);

        (await _queries.GetRecentUsageAsync(caller)).Should().ContainSingle();
    }

    [Fact]
    public async Task Pricing_import_persists_a_valid_file_and_rejects_an_invalid_one()
    {
        var ok = await _import.ImportAsync(ValidFile);
        ok.Success.Should().BeTrue();
        ok.ImportedModelCount.Should().Be(1);

        (await _queries.GetPricingAsync()).Should().ContainSingle(p => p.Model == "gpt-4o");

        var bad = await _import.ImportAsync(InvalidFile);
        bad.Success.Should().BeFalse();
        bad.Errors.Should().NotBeEmpty();

        // The invalid file added nothing; only the previously-imported model remains.
        (await _queries.GetPricingAsync()).Should().ContainSingle().Which.Model.Should().Be("gpt-4o");
    }

    private async Task AppendUsageAsync(string caller, Guid groupId, decimal cost)
    {
        await using var db = _factory.CreateDbContext();
        var evt = UsageEvent.Create(
            Guid.NewGuid().ToString(),
            CallerId.From(caller),
            groupId,
            BudgetSource.Group,
            "gpt-4o",
            tokensInput: 100,
            tokensOutput: 50,
            tokensCacheRead: 0,
            tokensCacheWrite: 0,
            TokenPrices.Create(2.5m, 10m, 1.25m),
            cost,
            "USD",
            runningSpendAfter: cost,
            BudgetPeriod.Current());

        await new UsageEventRepository(db).AppendAsync(evt);
    }

    private const string ValidFile = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            { "provider": "openai", "models": [
              { "model": "gpt-4o", "fetchedAt": "2026-06-24T12:00:00Z",
                "prices": { "input": 2.50, "output": 10.00, "cacheRead": 1.25, "cacheWrite": null } } ] }
          ]
        }
        """;

    private const string InvalidFile = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            { "provider": "openai", "models": [
              { "model": "bad", "fetchedAt": "2026-06-24T12:00:00Z",
                "prices": { "input": 2.50, "cacheRead": 1.25 } } ] }
          ]
        }
        """;
}
