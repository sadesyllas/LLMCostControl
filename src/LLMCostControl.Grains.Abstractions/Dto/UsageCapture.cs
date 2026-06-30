using LLMCostControl.Domain.Budgets;

namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// DTO for a usage capture request (§6.2.2).
/// </summary>
[GenerateSerializer]
public sealed record UsageCaptureRequest
{
    /// <summary>The model name reported by the gateway.</summary>
    [Id(0)]
    public required string Model { get; init; }

    /// <summary>
    /// Optional provider (<c>openai</c> | <c>anthropic</c> | <c>google</c>). When
    /// omitted or unrecognised, the grain infers it from the model name (§6.2.3).
    /// </summary>
    [Id(6)]
    public string? Provider { get; init; }

    /// <summary>Non-cached input token count.</summary>
    [Id(1)]
    public long TokensInput { get; init; }

    /// <summary>Generated output token count.</summary>
    [Id(2)]
    public long TokensOutput { get; init; }

    /// <summary>Cached input token count (provider cache hit).</summary>
    [Id(3)]
    public long TokensCacheRead { get; init; }

    /// <summary>Tokens written to the provider cache.</summary>
    [Id(4)]
    public long TokensCacheWrite { get; init; }

    /// <summary>Optional idempotency key (the gateway's requestId).</summary>
    [Id(5)]
    public string? RequestId { get; init; }
}

/// <summary>
/// DTO for the result of a usage capture (§6.2.2).
/// </summary>
[GenerateSerializer]
public sealed record UsageCaptureResult
{
    /// <summary>The caller id.</summary>
    [Id(0)]
    public required string CallerId { get; init; }

    /// <summary>The computed cost amount.</summary>
    [Id(1)]
    public required decimal CostAmount { get; init; }

    /// <summary>The currency of the cost.</summary>
    [Id(2)]
    public required string CostCurrency { get; init; }

    /// <summary>The caller's running spend after this capture.</summary>
    [Id(3)]
    public required decimal RunningSpendAmount { get; init; }

    /// <summary>The currency of the running spend.</summary>
    [Id(4)]
    public required string RunningSpendCurrency { get; init; }

    /// <summary>The remaining budget after this capture.</summary>
    [Id(5)]
    public required decimal RemainingAmount { get; init; }

    /// <summary>The currency of the remaining budget.</summary>
    [Id(6)]
    public required string RemainingCurrency { get; init; }

    /// <summary>The source of the effective budget.</summary>
    [Id(7)]
    public required BudgetSource BudgetSource { get; init; }

    /// <summary>The effective group id, if the source is a group.</summary>
    [Id(8)]
    public Guid? EffectiveGroupId { get; init; }
}
