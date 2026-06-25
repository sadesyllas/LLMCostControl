using System.Diagnostics;
using System.Diagnostics.Metrics;
using LLMCostControl.Domain.Budgets;

namespace LLMCostControl.Tracker.Api.Telemetry;

/// <summary>
/// Custom <see cref="Meter"/> instruments for the tracker API (§10.2). All
/// instruments carry <c>effective_group</c> and <c>budget_source</c> tags so
/// dashboards and alerts can be sliced by the group whose budget was in effect.
/// </summary>
public sealed class TrackerMetrics : IDisposable
{
    /// <summary>The meter name, matching the <c>AddMeter</c> registration in observability config.</summary>
    public const string MeterName = "LLMCostControl.Tracker.Api";

    private readonly Meter _meter;
    private readonly Counter<long> _checkCounter;
    private readonly Counter<long> _captureCounter;
    private readonly Histogram<double> _captureCostHistogram;

    /// <summary>Creates the meter and instruments.</summary>
    public TrackerMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _checkCounter = _meter.CreateCounter<long>("budget_check_total");
        _captureCounter = _meter.CreateCounter<long>("usage_capture_total");
        _captureCostHistogram = _meter.CreateHistogram<double>("usage_capture_cost");
    }

    /// <summary>
    /// Records a budget check request with outcome, budget source, and effective
    /// group tags.
    /// </summary>
    public void RecordCheck(bool allowed, BudgetSource source, Guid? groupId)
    {
        var tags = new TagList
        {
            { "outcome", allowed ? "allowed" : "denied" },
            { "budget_source", FormatSource(source) },
            { "effective_group", FormatGroup(groupId) },
        };
        _checkCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a usage capture with budget source, effective group, and cost tags.
    /// </summary>
    public void RecordCapture(BudgetSource source, Guid? groupId, double cost)
    {
        var tags = new TagList
        {
            { "budget_source", FormatSource(source) },
            { "effective_group", FormatGroup(groupId) },
        };
        _captureCounter.Add(1, tags);
        _captureCostHistogram.Record(cost, tags);
    }

    private static string FormatSource(BudgetSource source) =>
        source.ToString().ToLowerInvariant();

    private static string FormatGroup(Guid? groupId) =>
        groupId?.ToString() ?? "none";

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
