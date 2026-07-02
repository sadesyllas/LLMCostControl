using System;
using System.Threading.Tasks;
using FluentAssertions;
using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace LLMCostControl.Infrastructure.Tests;

/// <summary>
/// Integration tests verifying that non-empty database migration guards raise exceptions as expected.
/// </summary>
public class MigrationGuardTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private CostTrackerDbContext _db = null!;

    /// <summary>
    /// Starts the Postgres container and initializes the DbContext.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new CostTrackerDbContext(options);
    }

    /// <summary>
    /// Disposes the DbContext and stops the Postgres container.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddPricingVersioning_migration_fails_when_usage_events_table_is_not_empty()
    {
        var migrator = _db.Database.GetService<IMigrator>();

        // 1. Migrate up to the migration before AddPricingVersioning
        await migrator.MigrateAsync("20260701000000_AddMultiPeriodBudgets");

        // 2. Insert a dummy row into usage_events table using raw SQL
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO usage_events (\"EventId\", caller_id, \"Model\", provider, tokens_input, tokens_output, tokens_cache_read, tokens_cache_write, cost_amount, cost_currency, captured_at, unit_price_input, unit_price_output) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'alice@example.com', 'gpt-4o', 'openai', 100, 100, 0, 0, 0.03, 'USD', NOW(), 0.0001, 0.0002);");

        // 3. Try to migrate to 20260702000000_AddPricingVersioning
        Func<Task> act = async () => await migrator.MigrateAsync("20260702000000_AddPricingVersioning");

        // 4. Assert that it throws a migration exception caused by PostgresException with the expected error message
        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.And.MessageText.Should().Contain("Migration AddPricingVersioning failed: usage_events table is not empty");
    }
}
