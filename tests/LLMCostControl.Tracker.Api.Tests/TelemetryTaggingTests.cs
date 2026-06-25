using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Tracker.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Core;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>Custom Serilog sink to capture log events in-memory for assertion.</summary>
public class MemorySink : ILogEventSink
{
    /// <summary>The captured log events.</summary>
    public static readonly List<LogEvent> Events = new();

    /// <summary>Captures a log event.</summary>
    public void Emit(LogEvent logEvent)
    {
        lock (Events)
        {
            Events.Add(logEvent);
        }
    }
}

/// <summary>
/// Web application factory for testing telemetry tagging. Registers stubs and configures Serilog memory sink.
/// </summary>
public sealed class TelemetryWebAppFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT helper to issue tokens.</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Stub pricing store (shared with the silo).</summary>
    public StubPricingStore PricingStore { get; } = new();

    /// <summary>Stub budget store (shared with the silo).</summary>
    public StubBudgetStore BudgetStore { get; } = new();

    /// <summary>Stub usage event store (shared with the silo).</summary>
    public StubUsageEventStore UsageEventStore { get; } = new();

    /// <summary>Budget grain options.</summary>
    public BudgetGrainOptions BudgetOptions { get; } = new()
    {
        BudgetCacheTtl = TimeSpan.FromSeconds(1),
        AllowNonBudgetedUsers = true, // Allows capture when no budget is present
    };

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

        builder.UseSerilog((ctx, lc) =>
        {
            lc.WriteTo.Sink(new MemorySink())
              .Enrich.FromLogContext()
              .MinimumLevel.Debug();
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

/// <summary>
/// Integration tests verifying the effective-group telemetry tagging requirements (M15, §10.2).
/// Asserts that check/capture requests emit spans, metrics, and logs containing the expected
/// effective_group and budget_source tags.
/// </summary>
public sealed class TelemetryTaggingTests : IClassFixture<TelemetryWebAppFactory>
{
    private readonly TelemetryWebAppFactory _factory;

    /// <summary>Creates the test suite instance.</summary>
    public TelemetryTaggingTests(TelemetryWebAppFactory factory)
    {
        _factory = factory;
        lock (MemorySink.Events)
        {
            MemorySink.Events.Clear();
        }
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        var token = _factory.JwtHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Verifies that budget check and usage capture HTTP request spans, metrics, and logs
    /// are tagged with correct effective_group and budget_source tags for all budget sources.
    /// </summary>
    [Theory]
    [InlineData("Group")]
    [InlineData("UserOverride")]
    [InlineData("None")]
    public async Task Check_and_Capture_endpoints_propagate_tags_correctly(string budgetSourceType)
    {
        // 1. Seed budget according to test case
        var callerId = $"telemetry-{budgetSourceType.ToLower()}@example.com";
        Guid? groupId = null;

        if (budgetSourceType == "Group")
        {
            groupId = Guid.NewGuid();
            _factory.BudgetStore.SetBudget(callerId, EffectiveBudget.FromGroup(new Money(100m, "USD"), groupId.Value));
        }
        else if (budgetSourceType == "UserOverride")
        {
            _factory.BudgetStore.SetBudget(callerId, EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        }
        else
        {
            _factory.BudgetStore.SetBudget(callerId, EffectiveBudget.None());
        }

        _factory.PricingStore.SetPricing("gpt-4o", ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m)));

        // Set up Activity and Meter listeners
        var activities = new List<System.Diagnostics.Activity>();
        using var actListener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore" || source.Name == "LLMCostControl.Tracker.Api",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = act => { lock (activities) { activities.Add(act); } }
        };
        System.Diagnostics.ActivitySource.AddActivityListener(actListener);

        var measurements = new List<(string InstrumentName, object Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter.Name == "LLMCostControl.Tracker.Api")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, val, tags, state) =>
        {
            lock (measurements)
            {
                measurements.Add((inst.Name, val, tags.ToArray().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
            }
        });
        meterListener.SetMeasurementEventCallback<double>((inst, val, tags, state) =>
        {
            lock (measurements)
            {
                measurements.Add((inst.Name, val, tags.ToArray().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
            }
        });
        meterListener.Start();

        // 2. Perform check call
        var checkResp = await CreateClient().PostAsJsonAsync("/api/budget/check", new { callerId = callerId });
        checkResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Perform capture call
        var captureResp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = callerId,
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500 }
        });
        captureResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify traces (Activities) with a retry loop to eliminate race condition
        var expectedGroupStr = groupId?.ToString() ?? "None";
        List<System.Diagnostics.Activity> relevantActivities = new();

        for (int i = 0; i < 20; i++)
        {
            lock (activities)
            {
                relevantActivities = activities
                    .Where(a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn" &&
                                a.Tags.Any(t => t.Key == "caller_id" && (string?)t.Value == callerId))
                    .ToList();
            }

            if (relevantActivities.Count >= 2)
            {
                break;
            }
            await Task.Delay(100);
        }

        relevantActivities.Should().HaveCount(2, "because we expect exactly one check and one capture request for this caller");
        foreach (var act in relevantActivities)
        {
            act.Tags.Should().Contain(t => t.Key == "effective_group" && (string?)t.Value == expectedGroupStr);
            act.Tags.Should().Contain(t => t.Key == "budget_source" && (string?)t.Value == budgetSourceType);
        }

        // Verify metrics (exposing at least llm_budget_checks_total and llm_usage_captures_total)
        var checkMeasurements = measurements
            .Where(m => m.InstrumentName == "llm_budget_checks_total" &&
                        m.Tags.TryGetValue("caller_id", out var cid) && cid as string == callerId)
            .ToList();

        var captureMeasurements = measurements
            .Where(m => m.InstrumentName == "llm_usage_captures_total" &&
                        m.Tags.TryGetValue("caller_id", out var cid) && cid as string == callerId)
            .ToList();

        checkMeasurements.Should().NotBeEmpty();
        checkMeasurements.First().Tags.Should().ContainKey("effective_group").WhoseValue.Should().Be(expectedGroupStr);
        checkMeasurements.First().Tags.Should().ContainKey("budget_source").WhoseValue.Should().Be(budgetSourceType);

        captureMeasurements.Should().NotBeEmpty();
        captureMeasurements.First().Tags.Should().ContainKey("effective_group").WhoseValue.Should().Be(expectedGroupStr);
        captureMeasurements.First().Tags.Should().ContainKey("budget_source").WhoseValue.Should().Be(budgetSourceType);

        // Verify logs (Serilog LogContext)
        lock (MemorySink.Events)
        {
            var relevantLogs = MemorySink.Events
                .Where(e => (e.MessageTemplate.Text.Contains("Budget check for") || e.MessageTemplate.Text.Contains("Usage captured for")) &&
                            e.Properties.TryGetValue("CallerId", out var cid) && cid.ToString().Contains(callerId))
                .ToList();

            relevantLogs.Should().HaveCount(2);
            foreach (var log in relevantLogs)
            {
                log.Properties.Should().ContainKey("effective_group");
                log.Properties["effective_group"].ToString().Should().Contain(expectedGroupStr);

                log.Properties.Should().ContainKey("budget_source");
                log.Properties["budget_source"].ToString().Should().Contain(budgetSourceType);
            }
        }
    }
}
