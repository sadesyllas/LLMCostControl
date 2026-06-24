using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace LLMCostControl.Infrastructure.Repositories;

/// <summary>
/// Repository for <see cref="Group"/> entities: CRUD operations.
/// </summary>
public class GroupRepository
{
    private readonly CostTrackerDbContext _db;

    /// <summary>Creates the repository with the given DbContext.</summary>
    public GroupRepository(CostTrackerDbContext db) => _db = db;

    /// <summary>Gets a group by id, or null.</summary>
    public Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);

    /// <summary>Returns all groups.</summary>
    public Task<List<Group>> GetAllAsync(CancellationToken ct = default)
        => _db.Groups.ToListAsync(ct);

    /// <summary>Adds a new group and saves.</summary>
    public async Task AddAsync(Group group, CancellationToken ct = default)
    {
        await _db.Groups.AddAsync(group, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Updates an existing group and saves.</summary>
    public async Task UpdateAsync(Group group, CancellationToken ct = default)
    {
        _db.Groups.Update(group);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a group by id and saves.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Groups.Where(g => g.Id == id).ExecuteDeleteAsync(ct);
    }
}
