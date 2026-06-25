using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Tracker.Api.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog.Core;
using Serilog.Events;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for effective-group telemetry tagging (M15, §10.2): every
/// span, metric data point, and log event produced while servicing a
/// check/capture call carries <c>effective_group</c> and <c>budget_source</c>,
/// for each of <see cref="BudgetSource.Group"/>, <see cref="BudgetSource.UserOverride"/>,
/// and <see cref="BudgetSource.None"/>. Asserted via in-memory OTel exporters and
/// an in-memory Serilog sink.
/// </summary>
public sealed class EffectiveGroupTelemetryTests : IClassFixture<TelemetryApiFactory>
{
    private readonly TelemetryApiFactory _factory;

    public EffectiveGroupTelemetryTests(TelemetryApiFactory factory)
    {
        _factory = factory;
        _factory.ResetTelemetry();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.JwtHelper.GenerateToken());
        return client;
    }

    private void FlushTelemetry()
    {
        _factory.Services.GetRequiredService<TracerProvider>().ForceFlush(5000);
        _factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000);
    }

    [Fact]
    public async Task Check_for_group_budget_tags_span_metric_and_log()
    {
        var groupId = Guid.NewGuid();
        const string caller = "tele-group@example.com";
        _factory.BudgetStore.SetBudget(caller,
            EffectiveBudget.FromGroup(new Money(100m, "USD"), groupId));

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check", new { callerId = caller });
        await resp.Content.ReadAsStringAsync();
        FlushTelemetry();

        AssertAllSignalsTagged("Group", groupId.ToString(), "tracker.budget_checks");
    }

    [Fact]
    public async Task Check_for_user_override_tags_span_metric_and_log()
    {
        const string caller = "tele-override@example.com";
        _factory.BudgetStore.SetBudget(caller,
            EffectiveBudget.FromUserOverride(new Money(50m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check", new { callerId = caller });
        await resp.Content.ReadAsStringAsync();
        FlushTelemetry();

        AssertAllSignalsTagged("UserOverride", TrackerTelemetry.NoGroup, "tracker.budget_checks");
    }

    [Fact]
    public async Task Check_for_unbudgeted_caller_tags_span_metric_and_log_with_none()
    {
        const string caller = "tele-none@example.com";
        _factory.BudgetStore.SetBudget(caller, EffectiveBudget.None());

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check", new { callerId = caller });
        await resp.Content.ReadAsStringAsync();
        FlushTelemetry();

        AssertAllSignalsTagged("None", TrackerTelemetry.NoGroup, "tracker.budget_checks");
    }

    [Fact]
    public async Task Capture_tags_span_metric_and_log_with_effective_group()
    {
        var groupId = Guid.NewGuid();
        const string caller = "tele-capture@example.com";
        _factory.BudgetStore.SetBudget(caller,
            EffectiveBudget.FromGroup(new Money(100m, "USD"), groupId));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = caller,
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
        });
        await resp.Content.ReadAsStringAsync();
        FlushTelemetry();

        AssertAllSignalsTagged("Group", groupId.ToString(), "tracker.usage_captures");
        TagSetsFor("tracker.capture_cost")
            .Should().Contain(t => Matches(t, "Group", groupId.ToString()),
                "the capture cost histogram must also carry the effective-group tags.");
    }

    // --- assertions over the three telemetry signals ---

    private void AssertAllSignalsTagged(string expectedSource, string expectedGroup, string metricName)
    {
        // Span: the request activity carries both tags.
        var span = _factory.Activities.FirstOrDefault(a =>
            a.GetTagItem(TrackerTelemetry.BudgetSourceKey) as string == expectedSource);
        span.Should().NotBeNull("a span tagged with budget_source={0} must be exported.", expectedSource);
        (span!.GetTagItem(TrackerTelemetry.EffectiveGroupKey) as string).Should().Be(expectedGroup);

        // Metric: a data point carries both tags.
        TagSetsFor(metricName)
            .Should().Contain(t => Matches(t, expectedSource, expectedGroup),
                "a {0} metric data point must carry the effective-group tags.", metricName);

        // Log: an event carries both properties.
        _factory.LogSink.Events.Should().Contain(e =>
                Scalar(e, TrackerTelemetry.BudgetSourceKey) == expectedSource &&
                Scalar(e, TrackerTelemetry.EffectiveGroupKey) == expectedGroup,
            "a log event must carry the effective-group properties.");
    }

    private static bool Matches(IReadOnlyDictionary<string, object?> tags, string source, string group)
        => tags.TryGetValue(TrackerTelemetry.BudgetSourceKey, out var s) && s as string == source
        && tags.TryGetValue(TrackerTelemetry.EffectiveGroupKey, out var g) && g as string == group;

    private List<Dictionary<string, object?>> TagSetsFor(string metricName)
    {
        var result = new List<Dictionary<string, object?>>();

        foreach (var metric in _factory.Metrics.Where(m => m.Name == metricName))
        {
            foreach (var point in metric.GetMetricPoints())
            {
                var dict = new Dictionary<string, object?>();
                foreach (var tag in point.Tags)
                {
                    dict[tag.Key] = tag.Value;
                }

                result.Add(dict);
            }
        }

        return result;
    }

    private static string? Scalar(LogEvent logEvent, string key)
        => logEvent.Properties.TryGetValue(key, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;
}

/// <summary>In-memory Serilog sink capturing emitted log events for assertions.</summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly object _lock = new();

    /// <summary>A snapshot of the captured log events.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get { lock (_lock) { return _events.ToList(); } }
    }

    /// <summary>Clears the captured log events.</summary>
    public void Clear()
    {
        lock (_lock) { _events.Clear(); }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        lock (_lock) { _events.Add(logEvent); }
    }
}

/// <summary>
/// Web application factory for M15 telemetry tests. Mirrors the M13 factory
/// (real in-process Orleans silo, stub stores, mock OIDC) and additionally wires
/// in-memory OTel exporters (traces + metrics) and an in-memory Serilog sink so
/// the effective-group tags can be asserted on all three signals.
/// </summary>
public sealed class TelemetryApiFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT test helper for issuing gateway tokens.</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>Stub pricing store (shared with the silo).</summary>
    public StubPricingStore PricingStore { get; } = new();

    /// <summary>Stub budget store (shared with the silo).</summary>
    public StubBudgetStore BudgetStore { get; } = new();

    /// <summary>Stub usage event store (shared with the silo).</summary>
    public StubUsageEventStore UsageEventStore { get; } = new();

    /// <summary>In-memory Serilog sink capturing log events.</summary>
    public InMemoryLogSink LogSink { get; } = new();

    /// <summary>Exported spans.</summary>
    public List<Activity> Activities { get; } = [];

    /// <summary>Exported metrics.</summary>
    public List<Metric> Metrics { get; } = [];

    /// <summary>Budget grain options (shared with the silo).</summary>
    public BudgetGrainOptions BudgetOptions { get; } = new()
    {
        BudgetCacheTtl = TimeSpan.FromSeconds(1),
    };

    /// <summary>Resets the captured telemetry between tests.</summary>
    public void ResetTelemetry()
    {
        BudgetOptions.AllowNonBudgetedUsers = false;
        UsageEventStore.Reset();
        LogSink.Clear();
        Activities.Clear();
        Metrics.Clear();
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

            // Capture logs through the Serilog pipeline (picked up by UseObservability).
            services.AddSingleton<ILogEventSink>(LogSink);

            // Capture spans + metrics with in-memory OTel exporters.
            services.ConfigureOpenTelemetryTracerProvider((_, b) => b.AddInMemoryExporter(Activities));
            services.ConfigureOpenTelemetryMeterProvider((_, b) => b.AddInMemoryExporter(Metrics));
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
