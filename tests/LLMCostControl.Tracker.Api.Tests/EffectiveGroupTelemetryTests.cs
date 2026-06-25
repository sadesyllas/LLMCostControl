using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for M15 effective-group telemetry tagging (§10.2).
/// Verifies that <c>effective_group</c> and <c>budget_source</c> are attached
/// to the current activity (span) and recorded on metrics for each budget
/// source scenario (Group, UserOverride, None).
/// <para>
/// Log property tagging is implemented via <c>ILogger.BeginScope</c> in the
/// endpoint handlers; Serilog reads these scopes via its Extensions.Logging
/// bridge. Log property assertions require a Serilog in-memory sink (not in
/// the current package set) and are validated via console output in development.
/// </para>
/// <para>
/// Activity and metric listeners are process-global. Tests filter captured
/// data by URL path and unique <c>effective_group</c> values to remain stable
/// when multiple test classes run concurrently.
/// </para>
/// </summary>
public sealed class EffectiveGroupTelemetryTests : IClassFixture<TrackerApiFactory>
{
    private readonly TrackerApiFactory _factory;

    public EffectiveGroupTelemetryTests(TrackerApiFactory factory)
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

    /// <summary>
    /// Registers an <see cref="ActivityListener"/> that captures stopped activities
    /// from the given URL path that carry the <c>effective_group</c> tag.
    /// </summary>
    private static (IDisposable Listener, ConcurrentBag<Activity> Captured) StartActivityCapture(string urlPath)
    {
        var bag = new ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.GetTagItem("effective_group") is not null &&
                    a.GetTagItem("url.path")?.ToString() == urlPath)
                    bag.Add(a);
            },
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, bag);
    }

    /// <summary>
    /// Registers a <see cref="MeterListener"/> that captures measurements from
    /// the <c>LLMCostControl.Tracker.Api</c> meter, recording the instrument
    /// name, budget_source, and effective_group tags for each measurement.
    /// </summary>
    private static (IDisposable Listener, ConcurrentBag<(string Metric, string BudgetSource, string EffectiveGroup)> Captured)
        StartMetricCapture()
    {
        var bag = new ConcurrentBag<(string, string, string)>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name == "LLMCostControl.Tracker.Api")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var budgetSource = "";
            var effectiveGroup = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "budget_source") budgetSource = tag.Value?.ToString() ?? "";
                if (tag.Key == "effective_group") effectiveGroup = tag.Value?.ToString() ?? "";
            }
            bag.Add((instrument.Name, budgetSource, effectiveGroup));
        });
        listener.Start();
        return (listener, bag);
    }

    // ── budget_source = Group ──────────────────────────────────────────────────

    [Fact]
    public async Task Check_with_group_budget_tags_span_and_metric()
    {
        var groupId = Guid.NewGuid();
        _factory.BudgetStore.SetBudget("tel-group@example.com",
            EffectiveBudget.FromGroup(new Money(100m, "USD"), groupId));

        var (activityListener, activities) = StartActivityCapture("/api/budget/check");
        var (metricListener, metrics) = StartMetricCapture();
        using (activityListener) using (metricListener)
        {
            await CreateClient().PostAsJsonAsync("/api/budget/check",
                new { callerId = "tel-group@example.com" });
        }

        // The group id is unique per test run, so filtering by it gives a single match.
        var act = activities
            .Where(a => a.GetTagItem("effective_group")?.ToString() == groupId.ToString())
            .Should().ContainSingle().Which;
        act.GetTagItem("budget_source").Should().Be("Group");

        metrics.Should().Contain(m =>
            m.Metric == "budget.check.requests" &&
            m.BudgetSource == "Group" &&
            m.EffectiveGroup == groupId.ToString());
    }

    // ── budget_source = UserOverride ──────────────────────────────────────────

    [Fact]
    public async Task Check_with_user_override_tags_span_and_metric()
    {
        _factory.BudgetStore.SetBudget("tel-override@example.com",
            EffectiveBudget.FromUserOverride(new Money(50m, "USD")));

        var (activityListener, activities) = StartActivityCapture("/api/budget/check");
        var (metricListener, metrics) = StartMetricCapture();
        using (activityListener) using (metricListener)
        {
            await CreateClient().PostAsJsonAsync("/api/budget/check",
                new { callerId = "tel-override@example.com" });
        }

        // Use NotBeEmpty (not ContainSingle) since parallel tests may also emit
        // budget_source=UserOverride measurements with effective_group=none.
        activities
            .Where(a => a.GetTagItem("budget_source")?.ToString() == "UserOverride" &&
                        a.GetTagItem("effective_group")?.ToString() == "none")
            .Should().NotBeEmpty("activity should have budget_source=UserOverride effective_group=none");

        metrics
            .Where(m => m.Metric == "budget.check.requests" && m.BudgetSource == "UserOverride" && m.EffectiveGroup == "none")
            .Should().NotBeEmpty("metric should record budget_source=UserOverride effective_group=none");
    }

    // ── budget_source = None ──────────────────────────────────────────────────

    [Fact]
    public async Task Check_with_no_budget_tags_span_and_metric_as_none()
    {
        _factory.BudgetStore.SetBudget("tel-none@example.com", EffectiveBudget.None());
        _factory.BudgetOptions.AllowNonBudgetedUsers = true;

        var (activityListener, activities) = StartActivityCapture("/api/budget/check");
        var (metricListener, metrics) = StartMetricCapture();
        using (activityListener) using (metricListener)
        {
            await CreateClient().PostAsJsonAsync("/api/budget/check",
                new { callerId = "tel-none@example.com" });
        }

        _factory.BudgetOptions.AllowNonBudgetedUsers = false;

        activities
            .Where(a => a.GetTagItem("budget_source")?.ToString() == "None" &&
                        a.GetTagItem("effective_group")?.ToString() == "none")
            .Should().NotBeEmpty("activity should have budget_source=None effective_group=none");

        metrics
            .Where(m => m.Metric == "budget.check.requests" && m.BudgetSource == "None" && m.EffectiveGroup == "none")
            .Should().NotBeEmpty("metric should record budget_source=None effective_group=none");
    }

    // ── Capture path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Capture_with_group_budget_tags_span_and_metric()
    {
        var groupId = Guid.NewGuid();
        _factory.BudgetStore.SetBudget("tel-cap@example.com",
            EffectiveBudget.FromGroup(new Money(100m, "USD"), groupId));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));

        var (activityListener, activities) = StartActivityCapture("/api/usage/capture");
        var (metricListener, metrics) = StartMetricCapture();
        using (activityListener) using (metricListener)
        {
            await CreateClient().PostAsJsonAsync("/api/usage/capture", new
            {
                callerId = "tel-cap@example.com",
                model = "gpt-4o",
                tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
            });
        }

        var act = activities
            .Where(a => a.GetTagItem("effective_group")?.ToString() == groupId.ToString())
            .Should().ContainSingle().Which;
        act.GetTagItem("budget_source").Should().Be("Group");

        metrics.Should().Contain(m =>
            m.Metric == "usage.capture.requests" &&
            m.BudgetSource == "Group" &&
            m.EffectiveGroup == groupId.ToString());
    }
}
