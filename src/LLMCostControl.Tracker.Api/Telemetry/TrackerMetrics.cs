using System.Diagnostics.Metrics;

namespace LLMCostControl.Tracker.Api.Telemetry;

/// <summary>
/// OTel metric instruments for the Tracker API (§10.2). Records per-request
/// counters and cost histograms tagged with <c>budget_source</c> and
/// <c>effective_group</c> so dashboards and alerts can be sliced by group.
/// </summary>
public sealed class TrackerMetrics
{
    private readonly Counter<long> _checkRequests;
    private readonly Counter<long> _captureRequests;
    private readonly Histogram<double> _captureCost;

    /// <summary>
    /// Creates the metric instruments using the given meter factory (which
    /// creates meters tied to the host's lifetime).
    /// </summary>
    public TrackerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("LLMCostControl.Tracker.Api");
        _checkRequests = meter.CreateCounter<long>(
            "budget.check.requests",
            description: "Number of budget check requests.");
        _captureRequests = meter.CreateCounter<long>(
            "usage.capture.requests",
            description: "Number of usage capture requests.");
        _captureCost = meter.CreateHistogram<double>(
            "usage.capture.cost",
            unit: "USD",
            description: "Computed cost per usage capture.");
    }

    /// <summary>
    /// Records one budget check request, tagged with outcome and budget context.
    /// </summary>
    public void RecordCheck(bool allowed, string budgetSource, string effectiveGroup)
    {
        _checkRequests.Add(1,
            new KeyValuePair<string, object?>("allowed", allowed),
            new KeyValuePair<string, object?>("budget_source", budgetSource),
            new KeyValuePair<string, object?>("effective_group", effectiveGroup));
    }

    /// <summary>
    /// Records one usage capture request and its cost, tagged with model and
    /// budget context.
    /// </summary>
    public void RecordCapture(decimal cost, string model, string budgetSource, string effectiveGroup)
    {
        _captureRequests.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("budget_source", budgetSource),
            new KeyValuePair<string, object?>("effective_group", effectiveGroup));
        _captureCost.Record((double)cost,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("budget_source", budgetSource),
            new KeyValuePair<string, object?>("effective_group", effectiveGroup));
    }
}
