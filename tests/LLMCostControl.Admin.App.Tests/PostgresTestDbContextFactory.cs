using System;
using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// A test-only implementation of <see cref="IDbContextFactory{TContext}"/> backed by the
/// shared Testcontainers PostgreSQL database.
/// </summary>
public sealed class PostgresTestDbContextFactory : IDbContextFactory<CostTrackerDbContext>
{
    private readonly DbContextOptions<CostTrackerDbContext> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresTestDbContextFactory"/> class
    /// using the connection string from the shared fixture.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public PostgresTestDbContextFactory(string connectionString)
    {
        _options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    /// <summary>Creates a new DbContext instance.</summary>
    public CostTrackerDbContext CreateDbContext()
    {
        return new CostTrackerDbContext(_options);
    }

    /// <summary>Clears all data from all tables to ensure clean state between tests.</summary>
    public void ResetDatabase()
    {
        using var context = new CostTrackerDbContext(_options);
        context.Database.ExecuteSqlRaw("TRUNCATE TABLE group_memberships, group_budgets, user_budget_overrides, model_pricing, usage_events, groups CASCADE;");
    }
}
