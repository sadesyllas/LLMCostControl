namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Error response body for API errors.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>The error type.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Human-readable detail.</summary>
    public string? Detail { get; init; }
}
