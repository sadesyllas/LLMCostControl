using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="GroupMembership"/> entities.
/// </summary>
public class GroupMembershipRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public GroupMembershipRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>Returns all groups the given caller belongs to.</summary>
    public Task<List<Guid>> GetGroupIdsForCallerAsync(CallerId callerId, CancellationToken ct = default)
        => _db.GroupMemberships
            .Where(m => m.CallerId == callerId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

    /// <summary>Returns the caller ids that are members of the given group.</summary>
    public Task<List<CallerId>> GetCallersForGroupAsync(Guid groupId, CancellationToken ct = default)
        => _db.GroupMemberships
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.AddedAt)
            .Select(m => m.CallerId)
            .ToListAsync(ct);

    /// <summary>True when the given caller is already a member of the group.</summary>
    public Task<bool> ExistsAsync(Guid groupId, CallerId callerId, CancellationToken ct = default)
        => _db.GroupMemberships
            .AnyAsync(m => m.GroupId == groupId && m.CallerId == callerId, ct);

    /// <summary>Adds a membership and saves.</summary>
    public async Task AddAsync(GroupMembership membership, CancellationToken ct = default)
    {
        await _db.GroupMemberships.AddAsync(membership, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a membership (by group + caller) and saves.</summary>
    public async Task RemoveAsync(Guid groupId, CallerId callerId, CancellationToken ct = default)
    {
        await _db.GroupMemberships
            .Where(m => m.GroupId == groupId && m.CallerId == callerId)
            .ExecuteDeleteAsync(ct);
    }
}
