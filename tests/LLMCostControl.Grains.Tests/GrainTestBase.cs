using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using LLMCostControl.Grains.Implementations;
using LLMCostControl.Grains.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace LLMostControl.Grains.Tests;

/// <summary>
/// Shared static pricing store that both the test code and the silo's DI
/// container reference, ensuring they use the same instance.
/// </summary>
public static class SharedPricingStore
{
    /// <summary>The single shared store instance.</summary>
    public static StubPricingStore Instance { get; } = new();
}

/// <summary>
/// Shared test cluster fixture — one cluster for all grain tests.
/// </summary>
public sealed class GrainClusterFixture : IDisposable
{
    private readonly TestCluster _cluster;

    /// <summary>The test cluster.</summary>
    public TestCluster Cluster => _cluster;

    /// <summary>The shared stub pricing store.</summary>
    public StubPricingStore Store => SharedPricingStore.Instance;

    /// <summary>Creates and deploys a test cluster.</summary>
    public GrainClusterFixture()
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
/// Test silo configurator: registers memory storage, streams, the shared
/// <see cref="SharedPricingStore"/> instance, and the pricing cache.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    /// <summary>Configures the silo.</summary>
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorage("Default");
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryStreams("pricing");
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(SharedPricingStore.Instance);
            services.AddSingleton<IPricingStore>(SharedPricingStore.Instance);
            services.AddSingleton<IPricingCache, PricingCache>();
        });
    }
}

/// <summary>
/// Client configurator that adds the pricing stream provider.
/// </summary>
public sealed class TestClientConfigurator : IClientBuilderConfigurator
{
    /// <summary>Configures the client.</summary>
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("pricing");
    }
}

/// <summary>
/// Base class for all grain tests. Uses a shared <see cref="GrainClusterFixture"/>.
/// Each test should use unique model names to avoid cache interference.
/// </summary>
public abstract class GrainTestBase : IClassFixture<GrainClusterFixture>
{
    private readonly GrainClusterFixture _fixture;

    /// <summary>The grain factory (client).</summary>
    protected IGrainFactory GrainFactory => _fixture.Cluster.Client;

    /// <summary>The cluster client for stream access.</summary>
    protected IClusterClient Client => _fixture.Cluster.Client;

    /// <summary>The shared stub pricing store.</summary>
    protected StubPricingStore Store => _fixture.Store;

    /// <summary>Creates the test base with the shared fixture.</summary>
    protected GrainTestBase(GrainClusterFixture fixture)
    {
        _fixture = fixture;
    }
}
