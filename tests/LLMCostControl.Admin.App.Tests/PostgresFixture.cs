using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// A shared database fixture that manages a single Postgres container for the entire test collection.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("admin_app_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    /// <summary>The connection string of the test container.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Starts the container and runs EF Core database migrations.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new CostTrackerDbContext(options);
        await context.Database.MigrateAsync();
    }

    /// <summary>Stops the container.</summary>
    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
