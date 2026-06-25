using System.Net;
using System.Net.Http.Json;
using System.Text;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for the localhost pricing file import endpoint (M14, §8.3).
/// Uses SQLite in-memory for persistence and a stub publisher to verify events.
/// </summary>
public sealed class PricingImportEndpointTests : IClassFixture<PricingImportApiFactory>
{
    private readonly PricingImportApiFactory _factory;

    public PricingImportEndpointTests(PricingImportApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
        _factory.Publisher.Reset();
    }

    [Fact]
    public async Task Valid_file_persists_rows_and_publishes_events()
    {
        var json = """
        {
            "generatedAt": "2026-06-25T12:00:00Z",
            "currency": "USD",
            "unit": "per-1M-tokens",
            "providers": [
                {
                    "provider": "openai",
                    "models": [
                        {
                            "model": "gpt-4o",
                            "fetchedAt": "2026-06-25T12:00:00Z",
                            "prices": { "input": 2.50, "output": 10.00, "cacheRead": 1.25, "cacheWrite": null }
                        },
                        {
                            "model": "gpt-4o-mini",
                            "fetchedAt": "2026-06-25T12:00:00Z",
                            "prices": { "input": 0.15, "output": 0.60, "cacheRead": 0.075, "cacheWrite": null }
                        }
                    ]
                },
                {
                    "provider": "anthropic",
                    "models": [
                        {
                            "model": "claude-sonnet-4-20250514",
                            "fetchedAt": "2026-06-25T12:00:00Z",
                            "prices": { "input": 3.00, "output": 15.00, "cacheRead": 0.30, "cacheWrite": 3.75 }
                        }
                    ]
                }
            ]
        }
        """;

        var resp = await _factory.CreateClient().PostAsync(
            "/api/pricing/import",
            new StringContent(json, Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PricingImportResponse>();
        body!.Imported.Should().BeTrue();
        body.Count.Should().Be(3);
        body.UpdatedModels.Should().Contain(new[] { "gpt-4o", "gpt-4o-mini", "claude-sonnet-4-20250514" });

        _factory.Publisher.Published.Should().HaveCount(2);
        _factory.Publisher.Published.Should().Contain(p => p.Provider == Provider.OpenAI && p.Models.Contains("gpt-4o"));
        _factory.Publisher.Published.Should().Contain(p => p.Provider == Provider.Anthropic && p.Models.Contains("claude-sonnet-4-20250514"));

        var dbPricing = await _factory.GetPricingAsync();
        dbPricing.Should().HaveCount(3);
        dbPricing.Should().Contain(p => p.Model == "gpt-4o" && p.Prices.Input == 2.50m);
        dbPricing.Should().Contain(p => p.Model == "claude-sonnet-4-20250514" && p.Prices.Input == 3.00m);
    }

    [Fact]
    public async Task Invalid_file_returns_400_and_writes_nothing()
    {
        var json = """
        {
            "generatedAt": "2026-06-25T12:00:00Z",
            "currency": "USD",
            "unit": "per-1M-tokens",
            "providers": [
                {
                    "provider": "openai",
                    "models": [
                        {
                            "model": "gpt-4o",
                            "fetchedAt": "2026-06-25T12:00:00Z",
                            "prices": { "input": -1.0, "output": 10.00, "cacheRead": 1.25 }
                        }
                    ]
                }
            ]
        }
        """;

        var resp = await _factory.CreateClient().PostAsync(
            "/api/pricing/import",
            new StringContent(json, Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Be("invalid_file");

        _factory.Publisher.Published.Should().BeEmpty();

        var dbPricing = await _factory.GetPricingAsync();
        dbPricing.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_json_returns_400()
    {
        var resp = await _factory.CreateClient().PostAsync(
            "/api/pricing/import",
            new StringContent("{ not valid json", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Be("invalid_file");
    }

    [Fact]
    public async Task Endpoint_rejects_non_localhost_request()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.1");

        var resp = await client.PostAsync(
            "/api/pricing/import",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Be("forbidden");
    }
}

/// <summary>
/// Stub publisher that captures all published pricing-updated events for test
/// verification.
/// </summary>
public sealed class StubPricingPublisher : IPricingUpdatePublisher
{
    private readonly List<(Provider Provider, IReadOnlyList<string> Models)> _published = new();

    /// <summary>All published events, in order.</summary>
    public IReadOnlyList<(Provider Provider, IReadOnlyList<string> Models)> Published => _published;

    /// <summary>Clears all captured events.</summary>
    public void Reset() => _published.Clear();

    /// <inheritdoc />
    public Task PublishAsync(Provider provider, IReadOnlyList<string> updatedModels, CancellationToken ct = default)
    {
        _published.Add((provider, updatedModels));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Web application factory for M14 import endpoint tests. Configures SQLite
/// in-memory for persistence, a stub publisher, and no auth (the import endpoint
/// doesn't require gateway auth — localhost binding is the access control).
/// </summary>
public sealed class PricingImportApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection _connection = null!;

    /// <summary>Stub pricing publisher shared with the host.</summary>
    public StubPricingPublisher Publisher { get; } = new();

    /// <summary>Resets the in-memory database by clearing all pricing rows.</summary>
    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
        using var db = factory.CreateDbContext();
        db.ModelPricing.ExecuteDelete();
        db.SaveChanges();
    }

    /// <summary>Gets all pricing entries from the database.</summary>
    public async Task<List<ModelPricing>> GetPricingAsync()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.ModelPricing.ToListAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orleans:StorageConnectionString"] = "",
                ["GatewayAuth:JwksEndpoint"] = "",
                ["GatewayAuth:Issuer"] = "",
                ["GatewayAuth:Audience"] = "",
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
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new CostTrackerDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPricingUpdatePublisher>(Publisher);
            services.AddDbContextFactory<CostTrackerDbContext>(o => o.UseSqlite(_connection));

            // Middleware to simulate non-localhost requests via X-Test-Remote-IP header.
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Startup filter that injects a middleware to override <c>RemoteIpAddress</c>
/// from the <c>X-Test-Remote-IP</c> header, enabling non-localhost simulation in
/// tests.
/// </summary>
file sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, pipeline) =>
            {
                var testIp = context.Request.Headers["X-Test-Remote-IP"].FirstOrDefault();
                if (testIp is not null && IPAddress.TryParse(testIp, out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                await pipeline();
            });

            next(app);
        };
    }
}
