using System.Collections.Generic;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Result of validating and parsing a pricing file.
/// </summary>
public sealed class PricingFileParseResult
{
    /// <summary>True when the file was valid and entries were parsed.</summary>
    public bool IsValid { get; init; }

    /// <summary>The parsed model pricing entries (empty when invalid).</summary>
    public IReadOnlyList<ModelPricing> Entries { get; init; } = [];

    /// <summary>The file-level currency declared in the file (empty when invalid).</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>The file-level unit declared in the file (empty when invalid).</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Validation errors, if any (empty when valid).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Creates a successful parse result.</summary>
    public static PricingFileParseResult Success(
        IReadOnlyList<ModelPricing> entries,
        string currency,
        string unit) => new()
    {
        IsValid = true,
        Entries = entries,
        Currency = currency,
        Unit = unit,
    };

    /// <summary>Creates a failed parse result with the given errors.</summary>
    public static PricingFileParseResult Failure(IReadOnlyList<string> errors) => new()
    {
        IsValid = false,
        Errors = errors,
    };
}
