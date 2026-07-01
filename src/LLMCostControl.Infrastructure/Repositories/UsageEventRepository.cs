using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Usage;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="UsageEvent"/> entities: the append-only audit
/// ledger. Supports insertion and querying by caller / period.
/// </summary>
public class UsageEventRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public UsageEventRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>
    /// Appends a usage event. Returns false (no insert) if an event with the
    /// same id already exists (idempotency guard).
    /// </summary>
    public async Task<bool> AppendAsync(UsageEvent evt, CancellationToken ct = default)
    {
        var exists = await _db.UsageEvents.AnyAsync(e => e.EventId == evt.EventId, ct);
        if (exists)
        {
            return false;
        }

        await _db.UsageEvents.AddAsync(evt, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Returns all usage events for a caller in a period, ordered by capture
    /// time.
    /// </summary>
    public Task<List<UsageEvent>> GetForCallerAsync(
        CallerId callerId,
        BudgetPeriod period,
        CancellationToken ct = default)
        => _db.UsageEvents
            .Include(e => e.PeriodAccruals)
            .Where(e => e.CallerId == callerId && e.PeriodAccruals.Any(a => a.PeriodType == period.PeriodType && a.PeriodKey == period.Key))
            .OrderBy(e => e.CapturedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Checks whether an event with the given id already exists.
    /// </summary>
    public Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
        => _db.UsageEvents.AnyAsync(e => e.EventId == eventId, ct);

    /// <summary>
    /// Returns the event with the given id, or null when not found.
    /// </summary>
    public Task<UsageEvent?> GetByIdAsync(string eventId, CancellationToken ct = default)
        => _db.UsageEvents
            .Include(e => e.PeriodAccruals)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

    /// <summary>Returns distinct caller IDs across all usage events.</summary>
    public Task<List<CallerId>> GetDistinctCallersAsync(CancellationToken ct = default)
        => _db.UsageEvents
            .Select(e => e.CallerId)
            .Distinct()
            .ToListAsync(ct);
}
