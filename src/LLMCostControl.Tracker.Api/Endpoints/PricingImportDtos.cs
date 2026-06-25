namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Response body for a successful pricing file import via
/// <c>POST /api/pricing/import</c> (M14, §8.3).
/// </summary>
public sealed class ImportSuccessResponse
{
    /// <summary>Total number of model pricing entries imported.</summary>
    public int ImportedCount { get; init; }
}

/// <summary>
/// Response body for a rejected pricing file import (HTTP 422).
/// </summary>
public sealed class ImportErrorResponse
{
    /// <summary>Validation errors that caused the import to be rejected.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
