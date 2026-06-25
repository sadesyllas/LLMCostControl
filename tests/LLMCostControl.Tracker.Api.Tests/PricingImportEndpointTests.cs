using System.Net;
using System.Net.Http.Json;
using System.Text;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for the localhost-only pricing import endpoint (M14, §8.3):
/// a valid file is persisted, published, and visible to <c>PricingGrain</c>; an
/// invalid file is rejected with 4xx and writes nothing; the endpoint is not
/// reachable from a non-localhost address.
/// </summary>
public sealed class PricingImportEndpointTests : IClassFixture<PricingImportApiFactory>
{
    private const string Loopback = "127.0.0.1";
    private const string Remote = "203.0.113.7";

    private readonly PricingImportApiFactory _factory;

    public PricingImportEndpointTests(PricingImportApiFactory factory)
    {
        _factory = factory;
        _factory.ResetImportState();
    }

    private static string ValidFile(string model = "gpt-4o") => $$"""
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "{{model}}",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": { "input": 2.50, "output": 10.00, "cacheRead": 1.25, "cacheWrite": null }
                }
              ]
            }
          ]
        }
        """;

    private static HttpRequestMessage ImportRequest(string body, string remoteIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pricing/import")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TestRemoteIpStartupFilter.HeaderName, remoteIp);
        return request;
    }

    [Fact]
    public async Task Valid_file_from_localhost_persists_publishes_and_updates_grains()
    {
        var resp = await _factory.CreateClient()
            .SendAsync(ImportRequest(ValidFile("gpt-4o"), Loopback));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PricingImportResponse>();
        body!.ImportedModelCount.Should().Be(1);
        body.AffectedProviders.Should().ContainSingle().Which.Should().Be("OpenAI");

        // Rows persisted (visible via the store the grains read from).
        var stored = await _factory.PricingStore.GetByModelAsync("gpt-4o");
        stored.Should().NotBeNull();
        stored!.Prices.Input.Should().Be(2.50m);

        // Stream event published with the affected model.
        _factory.Publisher.Calls.Should().ContainSingle();
        var (provider, models) = _factory.Publisher.Calls[0];
        provider.Should().Be(Provider.OpenAI);
        models.Should().ContainSingle().Which.Should().Be("gpt-4o");

        // Imported value is visible to the PricingGrain.
        var grain = _factory.Services.GetRequiredService<IGrainFactory>()
            .GetGrain<IPricingGrain>("gpt-4o");
        var pricing = await grain.GetPricingAsync();
        pricing.Should().NotBeNull();
        pricing!.Input.Should().Be(2.50m);
        pricing.Output.Should().Be(10.00m);
        pricing.CacheRead.Should().Be(1.25m);
    }

    [Fact]
    public async Task Invalid_file_from_localhost_is_rejected_and_writes_nothing()
    {
        // Missing the mandatory 'output' price → atomic reject.
        const string invalid = """
            {
              "generatedAt": "2026-06-24T12:00:00Z",
              "currency": "USD",
              "unit": "per-1M-tokens",
              "providers": [
                {
                  "provider": "openai",
                  "models": [
                    { "model": "bad-model", "fetchedAt": "2026-06-24T12:00:00Z",
                      "prices": { "input": 2.50, "cacheRead": 1.25 } }
                  ]
                }
              ]
            }
            """;

        var resp = await _factory.CreateClient()
            .SendAsync(ImportRequest(invalid, Loopback));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<PricingImportErrorResponse>();
        body!.Error.Should().Be("invalid_pricing_file");
        body.Errors.Should().NotBeEmpty();

        (await _factory.PricingStore.GetByModelAsync("bad-model")).Should().BeNull();
        _factory.PricingStore.Count.Should().Be(0, "an invalid file must not write any rows.");
        _factory.Publisher.Calls.Should().BeEmpty("no stream event on a rejected import.");
    }

    [Fact]
    public async Task Endpoint_is_not_reachable_from_a_non_localhost_address()
    {
        var resp = await _factory.CreateClient()
            .SendAsync(ImportRequest(ValidFile("remote-model"), Remote));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _factory.PricingStore.GetByModelAsync("remote-model")).Should().BeNull();
        _factory.PricingStore.Count.Should().Be(0, "a non-localhost request must not write any rows.");
        _factory.Publisher.Calls.Should().BeEmpty("a non-localhost request must not publish.");
    }
}

/// <summary>
/// In-memory <see cref="IPricingWriter"/> that writes into the shared
/// <see cref="StubPricingStore"/> the grains read from, so an import is visible
/// to the read path without a database.
/// </summary>
public sealed class StubPricingWriter : IPricingWriter
{
    private readonly StubPricingStore _store;

    public StubPricingWriter(StubPricingStore store) => _store = store;

    public Task ReplaceProviderPricingAsync(
        Provider provider,
        IReadOnlyCollection<ModelPricing> entries,
        CancellationToken ct = default)
    {
        _store.ReplaceProvider(provider, entries);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Recording <see cref="IPricingUpdatePublisher"/> that captures each publish so
/// tests can assert the <c>pricing-updated</c> event was (or was not) emitted.
/// </summary>
public sealed class RecordingPricingPublisher : IPricingUpdatePublisher
{
    private readonly List<(Provider Provider, IReadOnlyList<string> Models)> _calls = [];

    /// <summary>The recorded publish calls.</summary>
    public IReadOnlyList<(Provider Provider, IReadOnlyList<string> Models)> Calls => _calls;

    /// <summary>Clears the recorded calls.</summary>
    public void Reset() => _calls.Clear();

    public Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
    {
        _calls.Add((provider, updatedModels));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test middleware (installed at the front of the pipeline) that sets the
/// connection's remote IP address from a request header, so the
/// <see cref="LocalhostOnlyEndpointFilter"/> can be exercised over the in-memory
/// test server (which leaves the remote IP unset by default).
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    /// <summary>Header carrying the remote IP to simulate.</summary>
    public const string HeaderName = "X-Test-RemoteIp";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                && IPAddress.TryParse(value.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await nextMiddleware();
        });

        next(app);
    };
}

/// <summary>
/// Web application factory for the M14 pricing import tests. Wires an in-process
/// Orleans silo with stub stores, a stub pricing writer + recording publisher,
/// a mock OIDC server (so the host boots identically to the M13 tests), and the
/// test remote-IP middleware.
/// </summary>
public sealed class PricingImportApiFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT test helper (the host requires a configured issuer to boot).</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Stub pricing store shared between the writer (import) and the grains (read).</summary>
    public StubPricingStore PricingStore { get; } = new();

    /// <summary>Recording publisher capturing pricing-updated events.</summary>
    public RecordingPricingPublisher Publisher { get; } = new();

    /// <summary>Stub budget store (shared with the silo).</summary>
    public StubBudgetStore BudgetStore { get; } = new();

    /// <summary>Stub usage event store (shared with the silo).</summary>
    public StubUsageEventStore UsageEventStore { get; } = new();

    /// <summary>Budget grain options (shared with the silo).</summary>
    public BudgetGrainOptions BudgetOptions { get; } = new()
    {
        BudgetCacheTtl = TimeSpan.FromSeconds(1),
    };

    /// <summary>Resets the import-related stub state between tests.</summary>
    public void ResetImportState()
    {
        PricingStore.Clear();
        Publisher.Reset();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _oidcServer = new MockOidcServer(JwtHelper);
        _oidcServer.Start();

        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GatewayAuth:JwksEndpoint"] = _oidcServer.DiscoveryUrl,
                ["GatewayAuth:Issuer"] = _oidcServer.Issuer,
                ["GatewayAuth:Audience"] = _oidcServer.Audience,
                ["Orleans:StorageConnectionString"] = "",
            });
        });

        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(PricingStore);
            services.AddSingleton<IPricingStore>(PricingStore);
            services.AddSingleton<IPricingCache, PricingCache>();
            services.AddSingleton(BudgetStore);
            services.AddSingleton<IBudgetStore>(BudgetStore);
            services.AddSingleton(UsageEventStore);
            services.AddSingleton<IUsageEventStore>(UsageEventStore);
            services.AddSingleton(BudgetOptions);

            // Import write path: stub writer (into the shared store) + recording publisher.
            services.AddSingleton<IPricingWriter>(new StubPricingWriter(PricingStore));
            services.AddSingleton<IPricingUpdatePublisher>(Publisher);

            // Lets LocalhostOnlyEndpointFilter be exercised over the test server.
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _oidcServer?.Dispose();
            JwtHelper.Dispose();
        }

        base.Dispose(disposing);
    }
}
