using System.Diagnostics;
using System.Diagnostics.Metrics;
using LLMCostControl.Domain.Budgets;
using Serilog.Context;

namespace LLMCostControl.Tracker.Api.Telemetry;

/// <summary>
/// Owns the tracker's custom <see cref="Meter"/> and the helpers that tag the
/// current span, metric data points, and log events with the effective-group
/// budget context (§10.2). The effective group and budget source are resolved by
/// the <c>UserBudgetGrain</c> at decision time (the same values persisted to the
/// audit row, §9.4) and propagated here, so every telemetry signal emitted while
/// servicing a <c>check</c>/<c>capture</c> call can be sliced by the group-level
/// budget the decision was made against.
/// </summary>
public sealed class TrackerTelemetry : IDisposable
{
    /// <summary>The OTel service name; also the <see cref="Meter"/> name (must match the registered meter).</summary>
    public const string ServiceName = "LLMCostControl.Tracker.Api";

    /// <summary>Telemetry tag / log-property key for the effective group.</summary>
    public const string EffectiveGroupKey = "effective_group";

    /// <summary>Telemetry tag / log-property key for the budget source.</summary>
    public const string BudgetSourceKey = "budget_source";

    /// <summary>Tag value used when no group budget is in effect (override / none).</summary>
    public const string NoGroup = "none";

    private readonly Meter _meter;
    private readonly Counter<long> _budgetChecks;
    private readonly Counter<long> _usageCaptures;
    private readonly Histogram<double> _captureCost;

    /// <summary>Creates the telemetry, building the meter and its instruments.</summary>
    public TrackerTelemetry()
    {
        _meter = new Meter(ServiceName);
        _budgetChecks = _meter.CreateCounter<long>(
            "tracker.budget_checks",
            unit: "{check}",
            description: "Budget check decisions, tagged by effective group, budget source and outcome.");
        _usageCaptures = _meter.CreateCounter<long>(
            "tracker.usage_captures",
            unit: "{capture}",
            description: "Usage capture decisions, tagged by effective group and budget source.");
        _captureCost = _meter.CreateHistogram<double>(
            "tracker.capture_cost",
            unit: "{cost}",
            description: "Cost accrued per usage capture, tagged by effective group and budget source.");
    }

    /// <summary>
    /// Formats an effective group id for telemetry, using <see cref="NoGroup"/>
    /// when there is no group (override / none) so the tag is always present.
    /// </summary>
    public static string FormatGroup(Guid? effectiveGroupId)
        => effectiveGroupId?.ToString() ?? NoGroup;

    /// <summary>
    /// Tags the current activity (span) with the effective-group budget context
    /// and pushes the same values into the Serilog log context, so every span and
    /// log event for the rest of the request carries <c>effective_group</c> and
    /// <c>budget_source</c>. Dispose the returned scope to pop the log-context
    /// properties at the end of the request.
    /// </summary>
    public IDisposable EnterEffectiveGroupScope(Guid? effectiveGroupId, BudgetSource source)
    {
        var group = FormatGroup(effectiveGroupId);
        var sourceName = source.ToString();

        var activity = Activity.Current;
        activity?.SetTag(EffectiveGroupKey, group);
        activity?.SetTag(BudgetSourceKey, sourceName);

        var popGroup = LogContext.PushProperty(EffectiveGroupKey, group);
        var popSource = LogContext.PushProperty(BudgetSourceKey, sourceName);
        return new CompositeScope(popSource, popGroup);
    }

    /// <summary>
    /// Records a budget check decision metric, tagged with the effective group,
    /// budget source, and allow/deny outcome.
    /// </summary>
    public void RecordBudgetCheck(bool allowed, Guid? effectiveGroupId, BudgetSource source)
    {
        _budgetChecks.Add(
            1,
            new KeyValuePair<string, object?>(EffectiveGroupKey, FormatGroup(effectiveGroupId)),
            new KeyValuePair<string, object?>(BudgetSourceKey, source.ToString()),
            new KeyValuePair<string, object?>("outcome", allowed ? "allowed" : "denied"));
    }

    /// <summary>
    /// Records a usage capture decision metric and the captured cost, both tagged
    /// with the effective group and budget source.
    /// </summary>
    public void RecordUsageCapture(decimal cost, Guid? effectiveGroupId, BudgetSource source)
    {
        var tags = new TagList
        {
            { EffectiveGroupKey, FormatGroup(effectiveGroupId) },
            { BudgetSourceKey, source.ToString() },
        };

        _usageCaptures.Add(1, tags);
        _captureCost.Record((double)cost, tags);
    }

    /// <summary>Disposes the meter and its instruments.</summary>
    public void Dispose() => _meter.Dispose();

    private sealed class CompositeScope : IDisposable
    {
        private readonly IDisposable _first;
        private readonly IDisposable _second;

        public CompositeScope(IDisposable first, IDisposable second)
        {
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            _first.Dispose();
            _second.Dispose();
        }
    }
}
