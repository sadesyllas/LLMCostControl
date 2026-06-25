namespace LLMCostControl.Grains.Options;

/// <summary>
/// Configuration for <see cref="Implementations.UserBudgetGrain"/>: the TTL for
/// the effective-budget cache and the fail-closed / allow-unbudgeted toggle
/// (§12.4, §7).
/// </summary>
public sealed class BudgetGrainOptions
{
    /// <summary>
    /// How long the grain caches the effective budget before re-reading from
    /// the DB. Default: 30 seconds (§12.4, Q5).
    /// </summary>
    public TimeSpan BudgetCacheTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Convenience property for configuration binding via the
    /// <c>BudgetCacheTtlSeconds</c> key (int seconds). Getting returns the
    /// whole seconds of <see cref="BudgetCacheTtl"/>; setting replaces
    /// <see cref="BudgetCacheTtl"/> with the given number of seconds.
    /// </summary>
    public int BudgetCacheTtlSeconds
    {
        get => (int)Math.Round(BudgetCacheTtl.TotalSeconds);
        set => BudgetCacheTtl = TimeSpan.FromSeconds(value);
    }

    /// <summary>
    /// When <c>false</c> (default), callers with no effective budget are
    /// denied (fail-closed, §7). When <c>true</c>, unbudgeted callers are
    /// allowed through the check — their spend is still recorded for audit
    /// but the check never gates them.
    /// </summary>
    public bool AllowNonBudgetedUsers { get; set; }
}
