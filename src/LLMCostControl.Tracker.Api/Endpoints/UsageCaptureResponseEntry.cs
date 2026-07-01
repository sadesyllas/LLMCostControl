namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Detailed budget entry per period in usage capture response (§6.2.2).
/// </summary>
public sealed class UsageCaptureResponseEntry
{
    /// <summary>The period type, e.g. "Monthly" or "Weekly".</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>The concrete period key, e.g. "2026-06" or "2026-W26".</summary>
    public string PeriodKey { get; init; } = string.Empty;

    /// <summary>The current running spend.</summary>
    public required MoneyDto RunningSpend { get; init; }

    /// <summary>The remaining budget.</summary>
    public required MoneyDto Remaining { get; init; }
}
