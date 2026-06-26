using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions.StreamEvents;
using LLMCostControl.Grains.Implementations;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace LLMCostControl.Grains.Tests;

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
/// Shared static budget store that both the test code and the silo's DI
/// container reference, ensuring they use the same instance.
/// </summary>
public static class SharedBudgetStore
{
    /// <summary>The single shared store instance.</summary>
    public static StubBudgetStore Instance { get; } = new();
}

/// <summary>
/// Shared static usage event store that both the test code and the silo's DI
/// container reference.
/// </summary>
public static class SharedUsageEventStore
{
    /// <summary>The single shared store instance.</summary>
    public static StubUsageEventStore Instance { get; } = new();
}

/// <summary>
/// Shared static budget options that both the test code and the silo's DI
/// container reference. Tests can mutate <see cref="AllowNonBudgetedUsers"/>
/// (tests run sequentially within the assembly).
/// </summary>
public static class SharedBudgetOptions
{
    /// <summary>The single shared options instance.</summary>
    public static BudgetGrainOptions Instance { get; } = new()
    {
        BudgetCacheTtl = TimeSpan.FromSeconds(1),
        AllowNonBudgetedUsers = false,
    };
}

/// <summary>
/// Shared static time provider for deterministic TTL and period-rollover
/// tests.
/// </summary>
public static class SharedTimeProvider
{
    /// <summary>The single shared time provider instance.</summary>
    public static FakeTimeProvider Instance { get; } =
        new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
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

    /// <summary>The shared stub budget store.</summary>
    public StubBudgetStore BudgetStore => SharedBudgetStore.Instance;

    /// <summary>The shared stub usage event store.</summary>
    public StubUsageEventStore UsageEventStore => SharedUsageEventStore.Instance;

    /// <summary>The shared budget options.</summary>
    public BudgetGrainOptions BudgetOptions => SharedBudgetOptions.Instance;

    /// <summary>The shared fake time provider.</summary>
    public FakeTimeProvider TimeProvider => SharedTimeProvider.Instance;

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
/// <see cref="SharedPricingStore"/> instance, budget store, budget options,
/// time provider, and the pricing cache.
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
            services.AddHostedService<PricingStreamSubscriber>();

            services.AddSingleton(SharedBudgetStore.Instance);
            services.AddSingleton<IBudgetStore>(SharedBudgetStore.Instance);
            services.AddSingleton(SharedUsageEventStore.Instance);
            services.AddSingleton<IUsageEventStore>(SharedUsageEventStore.Instance);
            services.AddSingleton(SharedBudgetOptions.Instance);
            services.AddSingleton<TimeProvider>(SharedTimeProvider.Instance);
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
/// Each test should use unique caller ids / model names to avoid cache
/// interference.
/// </summary>
public abstract class GrainTestBase : IClassFixture<GrainClusterFixture>, IAsyncLifetime
{
    private readonly GrainClusterFixture _fixture;

    /// <summary>The grain factory (client).</summary>
    protected IGrainFactory GrainFactory => _fixture.Cluster.Client;

    /// <summary>The cluster client for stream access.</summary>
    protected IClusterClient Client => _fixture.Cluster.Client;

    /// <summary>The shared stub pricing store.</summary>
    protected StubPricingStore Store => _fixture.Store;

    /// <summary>The shared stub budget store.</summary>
    protected StubBudgetStore BudgetStore => _fixture.BudgetStore;

    /// <summary>The shared stub usage event store.</summary>
    protected StubUsageEventStore UsageEventStore => _fixture.UsageEventStore;

    /// <summary>The shared budget options.</summary>
    protected BudgetGrainOptions BudgetOptions => _fixture.BudgetOptions;

    /// <summary>The shared fake time provider.</summary>
    protected FakeTimeProvider TimeProvider => _fixture.TimeProvider;

    /// <summary>Creates the test base with the shared fixture.</summary>
    protected GrainTestBase(GrainClusterFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Resets the shared mock stores, options, and time before every test run.</summary>
    public virtual Task InitializeAsync()
    {
        SharedPricingStore.Instance.Reset();
        SharedBudgetStore.Instance.Reset();
        SharedUsageEventStore.Instance.Reset();

        SharedBudgetOptions.Instance.BudgetCacheTtl = TimeSpan.FromSeconds(1);
        SharedBudgetOptions.Instance.AllowNonBudgetedUsers = false;

        SharedTimeProvider.Instance.SetUtcNow(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        return Task.CompletedTask;
    }

    /// <summary>Performs post-test cleanup.</summary>
    public virtual Task DisposeAsync() => Task.CompletedTask;
}
