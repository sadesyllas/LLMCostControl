using System;
using LLMCostControl.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// A test-only implementation of <see cref="IDbContextFactory{TContext}"/> backed by an
/// in-memory SQLite database. Ensures tables are created upon initialization.
/// </summary>
public sealed class SqliteTestDbContextFactory : IDbContextFactory<CostTrackerDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CostTrackerDbContext> _options;

    /// <summary>
    /// Creates the context factory, opens the connection, and creates all tables.
    /// </summary>
    public SqliteTestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new CostTrackerDbContext(_options);
        context.Database.EnsureCreated();
    }

    /// <summary>Creates a new DbContext instance sharing the opened SQLite connection.</summary>
    public CostTrackerDbContext CreateDbContext()
    {
        return new CostTrackerDbContext(_options);
    }

    /// <summary>Closes and disposes of the connection.</summary>
    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
