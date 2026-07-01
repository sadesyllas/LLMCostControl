namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Token counts sub-object for <see cref="UsageCaptureDto"/>.
/// </summary>
public sealed class TokenCountsDto
{
    /// <summary>Non-cached input token count.</summary>
    public long Input { get; set; }

    /// <summary>Generated output token count.</summary>
    public long Output { get; set; }

    /// <summary>Cached input token count (provider cache hit).</summary>
    public long CacheRead { get; set; }

    /// <summary>Tokens written to the provider cache.</summary>
    public long CacheWrite { get; set; }
}
