using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Minimal <see cref="IDbContextFactory{TContext}"/> for integration tests,
/// backing services with the Testcontainers connection options.
/// </summary>
public sealed class TestDbContextFactory(DbContextOptions<CostTrackerDbContext> options)
    : IDbContextFactory<CostTrackerDbContext>
{
    public CostTrackerDbContext CreateDbContext() => new(options);
}
