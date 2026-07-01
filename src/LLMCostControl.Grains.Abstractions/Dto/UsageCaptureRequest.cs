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
