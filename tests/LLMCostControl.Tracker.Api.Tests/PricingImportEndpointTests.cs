using System.Net;
using System.Net.Http.Json;
using System.Text;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for the localhost pricing file import endpoint (M14, §8.3).
/// Uses <see cref="PricingImportFactory"/> which wires a real in-process Orleans
/// silo with stub stores.
/// </summary>
public sealed class PricingImportEndpointTests : IClassFixture<PricingImportFactory>
{
    private readonly PricingImportFactory _factory;

    public PricingImportEndpointTests(PricingImportFactory factory)
    {
        _factory = factory;
        _factory.PricingWriter.Calls.Clear();
    }

    private static StringContent Json(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static string ValidFile(string model, decimal input = 2.5m, decimal output = 10m, decimal? cacheRead = 1.25m)
    {
        var cr = cacheRead.HasValue ? cacheRead.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
        return $$"""
        {
          "generatedAt": "2026-06-25T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [{
            "provider": "openai",
            "models": [{
              "model": "{{model}}",
              "fetchedAt": "2026-06-25T12:00:00Z",
              "prices": {
                "input": {{input.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                "output": {{output.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                "cacheRead": {{cr}},
                "cacheWrite": null
              }
            }]
          }]
        }
        """;
    }

    [Fact]
    public async Task Import_valid_file_returns_ok_with_model_count()
    {
        var resp = await _factory.CreateClient()
            .PostAsync("/api/pricing/import", Json(ValidFile("gpt-4o")));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ImportSuccessResponse>();
        body!.ImportedCount.Should().Be(1);
    }

    [Fact]
    public async Task Import_valid_file_persists_entries_via_writer()
    {
        var resp = await _factory.CreateClient()
            .PostAsync("/api/pricing/import", Json(ValidFile("gpt-4o-persist")));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.PricingWriter.Calls.Should().ContainSingle(c =>
            c.Provider == Provider.OpenAI &&
            c.Entries.Any(e => e.Model == "gpt-4o-persist"));
    }

    [Fact]
    public async Task Import_valid_file_updates_pricing_grain()
    {
        const string model = "gpt-4o-grain-test";

        // Activate the grain first so it subscribes to the pricing stream.
        var grainFactory = _factory.Services.GetRequiredService<IGrainFactory>();
        var grain = grainFactory.GetGrain<IPricingGrain>(model);
        var pricesBefore = await grain.GetPricingAsync();
        pricesBefore.Should().BeNull("model not imported yet");

        // Import a file that includes this model.
        var resp = await _factory.CreateClient()
            .PostAsync("/api/pricing/import", Json(ValidFile(model, 3m, 15m, 1.5m)));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Allow stream event delivery and grain cache refresh.
        await Task.Delay(400);

        var pricesAfter = await grain.GetPricingAsync();
        pricesAfter.Should().NotBeNull("grain should reflect imported pricing after stream event");
        pricesAfter!.Input.Should().Be(3m);
        pricesAfter.Output.Should().Be(15m);
    }

    [Fact]
    public async Task Import_invalid_file_returns_422_with_errors()
    {
        var badJson = """
        {
          "generatedAt": "2026-06-25T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [{
            "provider": "openai",
            "models": [{
              "model": "",
              "fetchedAt": "2026-06-25T12:00:00Z",
              "prices": { "input": 2.5, "output": 10.0, "cacheRead": 1.25 }
            }]
          }]
        }
        """;

        var resp = await _factory.CreateClient()
            .PostAsync("/api/pricing/import", Json(badJson));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<ImportErrorResponse>();
        body!.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Import_invalid_file_writes_no_rows()
    {
        var badJson = """
        {
          "generatedAt": "2026-06-25T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [{
            "provider": "unknown-provider",
            "models": [{ "model": "m1", "fetchedAt": "2026-06-25T12:00:00Z",
                         "prices": { "input": 1, "output": 1, "cacheRead": 0 } }]
          }]
        }
        """;

        await _factory.CreateClient().PostAsync("/api/pricing/import", Json(badJson));

        _factory.PricingWriter.Calls.Should().BeEmpty("validation failure must not write any rows");
    }

    [Fact]
    public async Task Import_endpoint_blocks_non_localhost_callers()
    {
        // Spin up a variant factory that fakes the remote IP to a non-loopback address.
        using var nonLocalFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, FakeNonLocalhostStartupFilter>());
        });

        var resp = await nonLocalFactory.CreateClient()
            .PostAsync("/api/pricing/import", Json(ValidFile("gpt-4o")));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// Web application factory for M14 pricing import endpoint tests.
/// Wires an in-process Orleans silo with stub pricing stores.
/// </summary>
public sealed class PricingImportFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT helper (not used for import endpoint, which is auth-free).</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Stub pricing store (shared with the silo as IPricingStore).</summary>
    public StubPricingStore PricingStore { get; } = new();

    /// <summary>Stub writer (captures calls + updates PricingStore for grain reads).</summary>
    public StubPricingStoreWriter PricingWriter { get; }

    /// <summary>Stub budget store.</summary>
    public StubBudgetStore BudgetStore { get; } = new();

    /// <summary>Stub usage event store.</summary>
    public StubUsageEventStore UsageEventStore { get; } = new();

    /// <summary>Initializes the factory and links the writer to the pricing store.</summary>
    public PricingImportFactory()
    {
        PricingWriter = new StubPricingStoreWriter(PricingStore);
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
            services.AddSingleton(PricingWriter);
            services.AddSingleton<IPricingStoreWriter>(PricingWriter);
            services.AddSingleton<PricingImportService>();
            services.AddSingleton(BudgetStore);
            services.AddSingleton<IBudgetStore>(BudgetStore);
            services.AddSingleton(UsageEventStore);
            services.AddSingleton<IUsageEventStore>(UsageEventStore);
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

/// <summary>
/// <see cref="IStartupFilter"/> that fakes <c>RemoteIpAddress</c> to a
/// non-loopback address, used to test the localhost restriction on the import
/// endpoint.
/// </summary>
file sealed class FakeNonLocalhostStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (ctx, nxt) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.1");
                await nxt(ctx);
            });
            next(app);
        };
}
