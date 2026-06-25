using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;
using FluentAssertions;
using LLMCostControl.Grains.Storage;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Startup filter for mocking connection info in integration tests.
/// </summary>
public sealed class TestStartupFilter : IStartupFilter
{
    /// <summary>Configures the startup pipeline middleware.</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Mock-Remote-Ip", out var ipStr))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ipStr!);
                }
                await nextMiddleware();
            });
            next(app);
        };
    }
}

/// <summary>
/// Web application factory for testing the pricing import endpoint (M14).
/// Spins up a Postgres container and applies migrations to support database writes.
/// </summary>
public sealed class PricingImportApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("tracker_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private MockOidcServer? _oidcServer;
    private bool _migrated;
    private readonly object _migrationLock = new();

    /// <summary>The JWT helper to issue tokens.</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Starts the Postgres container.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    /// <summary>Stops the Postgres container and OIDC server.</summary>
    public new async Task DisposeAsync()
    {
        _oidcServer?.Dispose();
        JwtHelper.Dispose();
        await _postgres.StopAsync();
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
                ["Orleans:StorageConnectionString"] = "", // Empty connection string forces local membership & memory grains
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
            services.AddSingleton<IStartupFilter, TestStartupFilter>();

            // Register database factory configured to connect to Postgres container for testing DbContext writes
            services.AddDbContextFactory<CostTrackerDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Replace the IPricingStore with the real DB-backed store so that grains load from our test Postgres DB
            services.Remove(services.Single(s => s.ServiceType == typeof(IPricingStore)));
            services.AddScoped<IPricingStore, Grains.Storage.PricingStore>();
        });
    }

    /// <summary>Runs EF migrations on the container database if not already done.</summary>
    public async Task EnsureMigratedAsync()
    {
        if (_migrated) return;
        lock (_migrationLock)
        {
            if (_migrated) return;
        }

        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        lock (_migrationLock)
        {
            _migrated = true;
        }
    }
}

/// <summary>
/// Integration tests for the localhost pricing file import endpoint (M14, §8.3).
/// </summary>
public sealed class PricingImportEndpointTests : IClassFixture<PricingImportApiFactory>
{
    private readonly PricingImportApiFactory _factory;

    /// <summary>Creates the test suite instance.</summary>
    public PricingImportEndpointTests(PricingImportApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task Import_valid_file_persists_to_db_and_updates_pricing_grains()
    {
        await _factory.EnsureMigratedAsync();
        var client = CreateClient();

        var validJson = @"
{
  ""generatedAt"": ""2026-06-25T12:00:00Z"",
  ""currency"": ""USD"",
  ""unit"": ""per-1M-tokens"",
  ""providers"": [
    {
      ""provider"": ""openai"",
      ""models"": [
        {
          ""model"": ""gpt-4o"",
          ""fetchedAt"": ""2026-06-25T12:00:00Z"",
          ""prices"": {
            ""input"": 2.50,
            ""output"": 10.00,
            ""cacheRead"": 1.25,
            ""cacheWrite"": 0.50
          }
        }
      ]
    }
  ]
}";

        // 1. Post valid JSON pricing file to /api/pricing/import (localhost)
        var response = await client.PostAsync("/api/pricing/import", new StringContent(validJson, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Query the DB directly to verify rows are persisted
        using (var scope = _factory.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var pricing = await db.ModelPricing.FirstOrDefaultAsync(p => p.Model == "gpt-4o");
            pricing.Should().NotBeNull();
            pricing!.Prices.Input.Should().Be(2.50m);
            pricing.Prices.Output.Should().Be(10.00m);
            pricing.Prices.CacheRead.Should().Be(1.25m);
            pricing.Prices.CacheWrite.Should().Be(0.50m);
            pricing.Currency.Should().Be("USD");
            pricing.Provider.Should().Be(Provider.OpenAI);
        }

        // 3. Query the PricingGrain to verify values become visible immediately
        var grainFactory = _factory.Services.GetRequiredService<IGrainFactory>();
        var pricingGrain = grainFactory.GetGrain<IPricingGrain>("gpt-4o");

        // Wait a small moment for the background stream task to process and update/invalidate cache
        await Task.Delay(200);

        var result = await pricingGrain.GetPricingAsync();
        result.Should().NotBeNull();
        result!.Input.Should().Be(2.50m);
        result.Output.Should().Be(10.00m);
        result.CacheRead.Should().Be(1.25m);
        result.CacheWrite.Should().Be(0.50m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Import_invalid_file_returns_bad_request_and_does_not_modify_db()
    {
        await _factory.EnsureMigratedAsync();
        var client = CreateClient();

        // Seed some base value first to verify no partial changes/overwrites on invalid import
        using (var scope = _factory.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            
            // Clean up existing gpt-4o pricing first
            var existing = await db.ModelPricing.FirstOrDefaultAsync(p => p.Model == "gpt-4o");
            if (existing is not null)
            {
                db.ModelPricing.Remove(existing);
                await db.SaveChangesAsync();
            }

            var basePricing = ModelPricing.Create(
                Provider.OpenAI, 
                "gpt-4o", 
                TokenPrices.Create(5.00m, 15.00m, null, null), 
                currency: "USD", 
                unit: "per-1M-tokens", 
                fetchedAt: DateTimeOffset.UtcNow);

            await db.ModelPricing.AddAsync(basePricing);
            await db.SaveChangesAsync();
        }

        var invalidJson = @"
{
  ""generatedAt"": ""2026-06-25T12:00:00Z"",
  ""currency"": ""USD"",
  ""unit"": ""per-1M-tokens"",
  ""providers"": [
    {
      ""provider"": ""openai"",
      ""models"": [
        {
          ""model"": ""gpt-4o"",
          ""fetchedAt"": ""2026-06-25T12:00:00Z"",
          ""prices"": {
            ""input"": -1.00,
            ""output"": 10.00,
            ""cacheRead"": 1.25
          }
        }
      ]
    }
  ]
}";

        // 1. Post invalid JSON pricing file to /api/pricing/import (localhost)
        var response = await client.PostAsync("/api/pricing/import", new StringContent(invalidJson, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Be("validation_failed");
        error.Detail.Should().Contain("input' price cannot be negative");

        // 2. Verify that the DB has not been updated (retains the original seed pricing)
        using (var scope = _factory.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var pricing = await db.ModelPricing.FirstOrDefaultAsync(p => p.Model == "gpt-4o");
            pricing.Should().NotBeNull();
            pricing!.Prices.Input.Should().Be(5.00m); // Kept original input price
            pricing.Prices.Output.Should().Be(15.00m);
        }
    }

    [Fact]
    public async Task Import_from_non_localhost_returns_forbidden()
    {
        await _factory.EnsureMigratedAsync();
        var client = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pricing/import");
        request.Headers.Add("X-Mock-Remote-Ip", "192.168.1.100");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
