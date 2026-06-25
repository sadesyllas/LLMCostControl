using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Grains.Storage;

namespace LLMostControl.Grains.Tests;

/// <summary>
/// In-memory stub <see cref="IUsageEventStore"/> for grain tests. Stores
/// events in a dictionary keyed by event id, with an idempotency guard on
/// append.
/// </summary>
public sealed class StubUsageEventStore : IUsageEventStore
{
    private readonly Dictionary<string, UsageEvent> _events = new(StringComparer.Ordinal);
    private int _appendCallCount;

    /// <summary>Number of times <see cref="AppendAsync"/> was called.</summary>
    public int AppendCallCount => _appendCallCount;

    /// <summary>All stored events.</summary>
    public IReadOnlyCollection<UsageEvent> Events => _events.Values;

    /// <summary>Resets the store to empty.</summary>
    public void Reset()
    {
        _events.Clear();
        _appendCallCount = 0;
    }

    /// <summary>Checks whether an event with the given id exists.</summary>
    public Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
        => Task.FromResult(_events.ContainsKey(eventId));

    /// <summary>Returns the event with the given id, or null.</summary>
    public Task<UsageEvent?> GetByIdAsync(string eventId, CancellationToken ct = default)
    {
        _events.TryGetValue(eventId, out var evt);
        return Task.FromResult(evt);
    }

    /// <summary>
    /// Appends the event. Returns false if an event with the same id already
    /// exists (idempotency guard).
    /// </summary>
    public Task<bool> AppendAsync(UsageEvent evt, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _appendCallCount);
        if (_events.ContainsKey(evt.EventId))
        {
            return Task.FromResult(false);
        }

        _events[evt.EventId] = evt;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns all events for a caller in a period, ordered by capture time.
    /// </summary>
    public Task<List<UsageEvent>> GetForCallerAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
    {
        var results = _events.Values
            .Where(e => e.CallerId == callerId && e.Period == period)
            .OrderBy(e => e.CapturedAt)
            .ToList();
        return Task.FromResult(results);
    }
}
