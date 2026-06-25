namespace LLMostControl.Grains.Tests;

/// <summary>
/// A minimal <see cref="TimeProvider"/> for tests that lets the test advance
/// time deterministically. Used by <c>UserBudgetGrain</c> for TTL cache and
/// budget-period rollover testing.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>Creates the provider set to the given start time.</summary>
    public FakeTimeProvider(DateTimeOffset start) => _now = start;

    /// <summary>Advances the clock to the given UTC time.</summary>
    public void SetUtcNow(DateTimeOffset now) => _now = now;

    /// <summary>Advances the clock by the given duration.</summary>
    public void Advance(TimeSpan delta) => _now += delta;

    /// <summary>Returns the current fake UTC time.</summary>
    public override DateTimeOffset GetUtcNow() => _now;
}
