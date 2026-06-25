using System.Diagnostics.Metrics;

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
    public void RecordBudgetCheck(string callerId, string outcome, string effectiveGroup, string budgetSource)
    {
        _checkRequests.Add(1, new KeyValuePair<string, object?>[]
        {
            new("caller_id", callerId),
            new("outcome", outcome),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource)
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
        string budgetSource)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("caller_id", callerId),
            new("outcome", outcome),
            new("model", model),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource)
        };

        _captureRequests.Add(1, tags);

        if (outcome == "accepted")
        {
            var costTags = new KeyValuePair<string, object?>[]
            {
                new("caller_id", callerId),
                new("model", model),
                new("currency", currency),
                new("effective_group", effectiveGroup),
                new("budget_source", budgetSource)
            };
            _captureCost.Add((double)cost, costTags);

            RecordTokens(callerId, model, "input", inputTokens, effectiveGroup, budgetSource);
            RecordTokens(callerId, model, "output", outputTokens, effectiveGroup, budgetSource);
            RecordTokens(callerId, model, "cache_read", cacheReadTokens, effectiveGroup, budgetSource);
            RecordTokens(callerId, model, "cache_write", cacheWriteTokens, effectiveGroup, budgetSource);
        }
    }

    private void RecordTokens(string callerId, string model, string type, long count, string effectiveGroup, string budgetSource)
    {
        if (count <= 0) return;

        _tokensCaptured.Add(count, new KeyValuePair<string, object?>[]
        {
            new("caller_id", callerId),
            new("model", model),
            new("token_type", type),
            new("effective_group", effectiveGroup),
            new("budget_source", budgetSource)
        });
    }
}
