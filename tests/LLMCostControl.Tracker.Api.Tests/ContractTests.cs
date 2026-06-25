using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Contract tests (§13.4) that fix the gateway-facing API shape for
/// /api/budget/check and /api/usage/capture. These tests verify the exact JSON
/// schema (field names, types, nesting) so that an unintended breaking change
/// is caught in CI.
/// </summary>
public sealed class ContractTests : IClassFixture<ContractApiFactory>
{
    private readonly ContractApiFactory _factory;

    public ContractTests(ContractApiFactory factory)
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

    // ── Check endpoint contract ──

    [Fact]
    public async Task Check_response_has_required_fields()
    {
        _factory.BudgetStore.SetBudget("contract@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "contract@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;

        json["allowed"]!.GetValue<bool>().Should().BeTrue();
        json["callerId"]!.GetValue<string>().Should().Be("contract@example.com");

        json["effectiveBudget"]!["amount"].Should().NotBeNull();
        json["effectiveBudget"]!["currency"].Should().NotBeNull();
        json["runningSpend"]!["amount"].Should().NotBeNull();
        json["runningSpend"]!["currency"].Should().NotBeNull();
        json["remaining"]!["amount"].Should().NotBeNull();
        json["remaining"]!["currency"].Should().NotBeNull();
    }

    [Fact]
    public async Task Check_response_when_denied_has_null_effectiveBudget()
    {
        _factory.BudgetStore.SetBudget("denied@example.com", EffectiveBudget.None());

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "denied@example.com" });

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        json["allowed"]!.GetValue<bool>().Should().BeFalse();
        json["effectiveBudget"].Should().BeNull();
    }

    [Fact]
    public async Task Check_error_for_missing_callerId()
    {
        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        json["error"]!.GetValue<string>().Should().Be("invalid_request");
        json["detail"].Should().NotBeNull();
    }

    [Fact]
    public async Task Check_without_auth_returns_401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "noauth@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Capture endpoint contract ──

    [Fact]
    public async Task Capture_response_has_required_fields()
    {
        _factory.BudgetStore.SetBudget("cap-contract@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o",
                TokenPrices.Create(2.5m, 10m, 1.25m)));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "cap-contract@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 200, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;

        json["callerId"]!.GetValue<string>().Should().Be("cap-contract@example.com");
        json["cost"]!["amount"].Should().NotBeNull();
        json["cost"]!["currency"].Should().NotBeNull();
        json["runningSpend"]!["amount"].Should().NotBeNull();
        json["runningSpend"]!["currency"].Should().NotBeNull();
        json["remaining"]!["amount"].Should().NotBeNull();
        json["remaining"]!["currency"].Should().NotBeNull();
    }

    [Fact]
    public async Task Capture_unknown_model_returns_error_envelope()
    {
        _factory.BudgetStore.SetBudget("cap-err@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "cap-err@example.com",
            model = "nonexistent",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        json["error"]!.GetValue<string>().Should().Be("unknown_model");
        json["detail"].Should().NotBeNull();
    }

    [Fact]
    public async Task Capture_error_for_missing_callerId()
    {
        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture",
            new { model = "gpt-4o", tokens = new { input = 1, output = 1, cacheRead = 0, cacheWrite = 0 } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        json["error"]!.GetValue<string>().Should().Be("invalid_request");
    }

    [Fact]
    public async Task Capture_error_for_missing_model()
    {
        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "cap-nomodel@example.com",
            tokens = new { input = 1, output = 1, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        json["error"]!.GetValue<string>().Should().Be("invalid_request");
    }

    // ── Pricing import endpoint contract ──
    // (covered by PricingImportEndpointTests.cs — contract tests focus on the
    //  gateway-facing check/capture API per §13.4)
}

/// <summary>
/// Web application factory for contract tests. Configures an in-process Orleans
/// silo with stub stores and mock OIDC server.
/// </summary>
public sealed class ContractApiFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    public JwtTestHelper JwtHelper { get; } = new();
    public StubPricingStore PricingStore { get; } = new();
    public StubBudgetStore BudgetStore { get; } = new();
    public StubUsageEventStore UsageEventStore { get; } = new();
    public BudgetGrainOptions BudgetOptions { get; } = new() { BudgetCacheTtl = TimeSpan.FromSeconds(1) };

    public void ResetStubs()
    {
        BudgetOptions.AllowNonBudgetedUsers = false;
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
