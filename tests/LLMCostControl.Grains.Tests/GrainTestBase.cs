using LLMCostControl.Grains.Implementations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;

namespace LLMostControl.Grains.Tests;

/// <summary>
/// Test silo configuration for grain integration tests. Registers the grain
/// implementations and in-memory storage.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    /// <summary>
    /// Configures the silo with memory storage and grain registrations.
    /// </summary>
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("Default");
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryStreams("pricing");
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<PricingGrain>();
            services.AddSingleton<UserBudgetGrain>();
        });
    }
}

/// <summary>
/// Base class for grain integration tests using <see cref="TestCluster"/>.
/// </summary>
public abstract class GrainTestBase : IDisposable
{
    private readonly TestCluster _cluster;

    /// <summary>The test cluster's grain factory (client).</summary>
    protected IGrainFactory GrainFactory => _cluster.Client;

    /// <summary>The test cluster's client for stream access.</summary>
    protected IClusterClient Client => _cluster.Client;

    /// <summary>Creates a test cluster with <see cref="TestSiloConfigurator"/>.</summary>
    protected GrainTestBase()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        _cluster = builder.Build();
        _cluster.Deploy();
    }

    /// <summary>Stops the test cluster.</summary>
    public void Dispose()
    {
        _cluster.StopAllSilos();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Configures the client with memory streams for stream tests.
/// </summary>
public sealed class TestClientConfigurator : IClientBuilderConfigurator
{
    /// <summary>Configures the client with the pricing stream provider.</summary>
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("pricing");
    }
}
