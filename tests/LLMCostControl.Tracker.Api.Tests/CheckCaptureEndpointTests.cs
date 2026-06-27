using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Grains.Tests;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for the check and capture endpoints (M13, §6.2.1, §6.2.2).
/// Uses <see cref="TrackerApiFactory"/> which wires up a real in-process Orleans
/// silo with stub stores, mock OIDC server, and test pricing.
/// </summary>
public sealed class CheckCaptureEndpointTests : IClassFixture<TrackerApiFactory>
{
    private readonly TrackerApiFactory _factory;

    public CheckCaptureEndpointTests(TrackerApiFactory factory)
    {
        _factory = factory;
        _factory.ResetStubs();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        var token = _factory.JwtHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void SeedPricing(string model, decimal input, decimal output, decimal? cacheRead = null)
    {
        _factory.PricingStore.SetPricing(model,
            ModelPricing.Create(Provider.OpenAI, model,
                TokenPrices.Create(input, output, cacheRead)));
    }

    private void SeedBudget(string callerId, EffectiveBudget budget)
    {
        _factory.BudgetStore.SetBudget(callerId, budget);
    }

    [Fact]
    public async Task Check_returns_allowed_with_budget()
    {
        SeedBudget("check-allow@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "check-allow@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BudgetCheckResponse>();
        body!.Allowed.Should().BeTrue();
        body.EffectiveBudget!.Amount.Should().Be(100m);
        body.RunningSpend.Amount.Should().Be(0m);
        body.Remaining.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Check_returns_denied_for_unbudgeted_fail_closed()
    {
        SeedBudget("check-deny@example.com", EffectiveBudget.None());

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "check-deny@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<BudgetCheckResponse>();
        body!.Allowed.Should().BeFalse();
        body.EffectiveBudget.Should().BeNull();
    }

    [Fact]
    public async Task Check_allows_unbudgeted_when_AllowNonBudgetedUsers_true()
    {
        SeedBudget("check-unbudgeted@example.com", EffectiveBudget.None());
        _factory.BudgetOptions.AllowNonBudgetedUsers = true;

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "check-unbudgeted@example.com" });

        var body = await resp.Content.ReadFromJsonAsync<BudgetCheckResponse>();
        body!.Allowed.Should().BeTrue();
        _factory.BudgetOptions.AllowNonBudgetedUsers = false;
    }

    [Fact]
    public async Task Capture_returns_computed_cost()
    {
        SeedBudget("capture-ok@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        SeedPricing("gpt-4o", 2.5m, 10m, 1.25m);

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "capture-ok@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 200, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<UsageCaptureResponse>();
        body!.Cost.Amount.Should().BePositive();
        body.RunningSpend.Amount.Should().Be(body.Cost.Amount);
    }

    [Fact]
    public async Task Capture_unknown_model_returns_400()
    {
        SeedBudget("capture-unknown@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "capture-unknown@example.com",
            model = "nonexistent",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Be("unknown_model");
    }

    [Fact]
    public async Task Capture_idempotent_on_duplicate_requestId()
    {
        SeedBudget("capture-idem@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        SeedPricing("gpt-4o", 2.5m, 10m);

        var payload = new
        {
            callerId = "capture-idem@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
            requestId = "req-idem-001",
        };

        var first = await CreateClient().PostAsJsonAsync("/api/usage/capture", payload);
        var firstBody = await first.Content.ReadFromJsonAsync<UsageCaptureResponse>();

        var second = await CreateClient().PostAsJsonAsync("/api/usage/capture", payload);
        var secondBody = await second.Content.ReadFromJsonAsync<UsageCaptureResponse>();

        secondBody!.RunningSpend.Amount.Should().Be(firstBody!.RunningSpend.Amount,
            "duplicate requestId must not double-accrue.");
    }

    [Fact]
    public async Task Check_without_auth_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/budget/check",
            new { callerId = "noauth@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// Web application factory for M13 endpoint tests. Configures an in-process
/// Orleans silo with stub stores, mock OIDC server, and test pricing.
/// </summary>
public sealed class TrackerApiFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT test helper for issuing tokens.</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Stub pricing store (shared with the silo).</summary>
    public StubPricingStore PricingStore { get; } = new();

    /// <summary>Stub budget store (shared with the silo).</summary>
    public StubBudgetStore BudgetStore { get; } = new();

    /// <summary>Stub usage event store (shared with the silo).</summary>
    public StubUsageEventStore UsageEventStore { get; } = new();

    /// <summary>Budget grain options (shared with the silo).</summary>
    public BudgetGrainOptions BudgetOptions { get; } = new()
    {
        BudgetCacheTtl = TimeSpan.FromSeconds(1),
    };

    /// <summary>Resets all stubs to a clean state.</summary>
    public void ResetStubs()
    {
        BudgetOptions.AllowNonBudgetedUsers = false;
        PricingStore.Reset();
        BudgetStore.Reset();
        UsageEventStore.Reset();
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
                ["Observability:TelemetryPepper"] = "test-telemetry-pepper",
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
            // Replace the real stores with stubs for the test silo.
            services.AddSingleton(PricingStore);
            services.AddSingleton<IPricingStore>(PricingStore);
            services.AddSingleton<IPricingCache, PricingCache>();
            services.AddSingleton(BudgetStore);
            services.AddSingleton<IBudgetStore>(BudgetStore);
            services.AddSingleton(UsageEventStore);
            services.AddSingleton<IUsageEventStore>(UsageEventStore);
            services.AddSingleton(BudgetOptions);
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
