using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LLMCostControl.Infrastructure.Data.Factories;

/// <summary>
/// Design-time factory for <see cref="CostTrackerDbContext"/>, used by EF Core
/// CLI tools (migrations, scaffolding) when no DI container is available.
/// </summary>
public class CostTrackerDbContextFactory : IDesignTimeDbContextFactory<CostTrackerDbContext>
{
    /// <summary>
    /// Creates a <see cref="CostTrackerDbContext"/> instance with a Npgsql
    /// connection string pointing at the local development Postgres.
    /// </summary>
    public CostTrackerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=llmcostcontrol;Username=llmcostcontrol;Password=llmcostcontrol")
            .Options;

        return new CostTrackerDbContext(options);
    }
}
