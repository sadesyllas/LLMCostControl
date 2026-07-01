using System.Diagnostics.Metrics;
using LLMCostControl.Observability;

namespace LLMCostControl.Tracker.Api.Observability;

/// <summary>
/// Custom OpenTelemetry metrics for the tracker API (§10.2).
/// Registers counters for budget checks, usage captures, cost, and tokens.
/// </summary>
public sealed class TrackerMetrics
{
    /// <summary>The name of the meter.</summary>
    public const string MeterName = "LLMCostControl.Tracker.Api";

    private readonly Counter<long> _checkRequests;
    private readonly Counter<long> _captureRequests;
    private readonly Counter<double> _captureCost;
    private readonly Counter<long> _tokensCaptured;

    /// <summary>
    /// Creates the tracker metrics using the given meter factory.
    /// </summary>
    /// <param name="meterFactory">The meter factory.</param>
    public TrackerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _checkRequests = meter.CreateCounter<long>(
            name: "llm_budget_checks_total",
            unit: "requests",
            description: "Total number of budget check requests.");

        _captureRequests = meter.CreateCounter<long>(
            name: "llm_usage_captures_total",
            unit: "requests",
            description: "Total number of usage capture requests.");

        _captureCost = meter.CreateCounter<double>(
            name: "llm_capture_cost_total",
            unit: "amount",
            description: "Total cost captured from usage.");

        _tokensCaptured = meter.CreateCounter<long>(
            name: "llm_tokens_captured_total",
            unit: "tokens",
            description: "Total number of tokens captured.");
    }

    /// <summary>
    /// Records a budget check event.
    /// </summary>
    /// <param name="callerId">The caller identifier.</param>
    /// <param name="outcome">The outcome: "allowed" or "denied".</param>
    /// <param name="effectiveGroup">The name of the effective group.</param>
    /// <param name="budgetSource">The source of the budget.</param>
    /// <param name="budgetPeriod">The binding budget period type.</param>
    public void RecordBudgetCheck(string callerId, string outcome, string effectiveGroup, string budgetSource, string budgetPeriod)
    {
        var anonCallerId = ObservabilityExtensions.AnonymizeCallerId(callerId);
        _checkRequests.Add(1, new KeyValuePair<string, object?>[]
        {
            new("caller_id", anonCallerId),
            new("outcome", outcome),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource),
            new("budget_period", budgetPeriod)
        });
    }

    /// <summary>
    /// Records a usage capture event.
    /// </summary>
    /// <param name="callerId">The caller identifier.</param>
    /// <param name="outcome">The outcome: "accepted" or "rejected".</param>
    /// <param name="model">The model name.</param>
    /// <param name="cost">The computed cost.</param>
    /// <param name="currency">The currency code.</param>
    /// <param name="inputTokens">The input tokens count.</param>
    /// <param name="outputTokens">The output tokens count.</param>
    /// <param name="cacheReadTokens">The cache read tokens count.</param>
    /// <param name="cacheWriteTokens">The cache write tokens count.</param>
    /// <param name="effectiveGroup">The name of the effective group.</param>
    /// <param name="budgetSource">The source of the budget.</param>
    /// <param name="budgetPeriod">The binding budget period type.</param>
    public void RecordUsageCapture(
        string callerId,
        string outcome,
        string model,
        decimal cost,
        string currency,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        string effectiveGroup,
        string budgetSource,
        string budgetPeriod)
    {
        var anonCallerId = ObservabilityExtensions.AnonymizeCallerId(callerId);
        var tags = new KeyValuePair<string, object?>[]
        {
            new("caller_id", anonCallerId),
            new("outcome", outcome),
            new("model", model),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource),
            new("budget_period", budgetPeriod)
        };

        _captureRequests.Add(1, tags);

        if (outcome == "accepted")
        {
            var costTags = new KeyValuePair<string, object?>[]
            {
                new("caller_id", anonCallerId),
                new("model", model),
                new("currency", currency),
                new("effective_group", effectiveGroup),
                new("budget_source", budgetSource),
                new("budget_period", budgetPeriod)
            };
            _captureCost.Add((double)cost, costTags);

            RecordTokens(anonCallerId, model, "input", inputTokens, effectiveGroup, budgetSource, budgetPeriod);
            RecordTokens(anonCallerId, model, "output", outputTokens, effectiveGroup, budgetSource, budgetPeriod);
            RecordTokens(anonCallerId, model, "cache_read", cacheReadTokens, effectiveGroup, budgetSource, budgetPeriod);
            RecordTokens(anonCallerId, model, "cache_write", cacheWriteTokens, effectiveGroup, budgetSource, budgetPeriod);
        }
    }

    private void RecordTokens(string anonCallerId, string model, string type, long count, string effectiveGroup, string budgetSource, string budgetPeriod)
    {
        if (count <= 0) return;

        _tokensCaptured.Add(count, new KeyValuePair<string, object?>[]
        {
            new("caller_id", anonCallerId),
            new("model", model),
            new("token_type", type),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource),
            new("budget_period", budgetPeriod)
        });
    }
}
