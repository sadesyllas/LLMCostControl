using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Infrastructure.Tests;

/// <summary>
/// Base class for repository integration tests. Spins up a Postgres container
/// via Testcontainers, applies EF migrations, and provides a fresh
/// <see cref="CostTrackerDbContext"/> per test.
/// </summary>
public abstract class RepositoryTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private CostTrackerDbContext _db = null!;

    /// <summary>The DbContext connected to the test Postgres container.</summary>
    protected CostTrackerDbContext Db => _db;

    /// <summary>Starts the container and applies migrations.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        _db = new CostTrackerDbContext(options);
        await _db.Database.MigrateAsync();
    }

    /// <summary>Stops the container and disposes the DbContext.</summary>
    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
