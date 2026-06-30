namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// xUnit collection definition for Postgres database tests.
/// </summary>
[CollectionDefinition("PostgresCollection")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
