using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Grains.Storage;

/// <summary>
/// Read/write-side interface for <see cref="UsageEvent"/> audit rows, safe for
/// use inside grains via a <c>IDbContextFactory</c>.
/// </summary>
public interface IUsageEventStore
{
    /// <summary>Checks whether an event with the given id already exists.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken ct = default);

    /// <summary>Returns the event with the given id, or null when not found.</summary>
    Task<UsageEvent?> GetByIdAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Appends a usage event. Returns false (no insert) if an event with the
    /// same id already exists (idempotency guard).
    /// </summary>
    Task<bool> AppendAsync(UsageEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Returns all usage events for a caller in a period, ordered by capture
    /// time.
    /// </summary>
    Task<List<UsageEvent>> GetForCallerAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default);
}

