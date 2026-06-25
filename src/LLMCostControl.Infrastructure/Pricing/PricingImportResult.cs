namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Result of a pricing file import performed by <see cref="PricingImportService"/>.
/// </summary>
public sealed class PricingImportResult
{
    /// <summary>True when the import succeeded and entries were persisted.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Number of model pricing entries imported (0 on failure).</summary>
    public int ImportedCount { get; init; }

    /// <summary>Validation errors that caused the import to fail (empty on success).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Creates a successful import result.</summary>
    public static PricingImportResult Success(int count) =>
        new() { IsSuccess = true, ImportedCount = count };

    /// <summary>Creates a failed import result with the given validation errors.</summary>
    public static PricingImportResult Failure(IReadOnlyList<string> errors) =>
        new() { IsSuccess = false, Errors = errors };
}
