using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// Links a caller to a group. A caller may belong to multiple groups; the
/// largest group budget applies (unless a per-user override exists).
/// </summary>
public class GroupMembership
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The group the caller belongs to.</summary>
    public Guid GroupId { get; init; }

    /// <summary>The caller that is a member of the group.</summary>
    public CallerId CallerId { get; init; }

    /// <summary>When the membership was created.</summary>
    public DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="GroupMembership"/> linking a caller to a group.
    /// </summary>
    public static GroupMembership Create(Guid groupId, CallerId callerId)
    {
        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("Group id cannot be empty.", nameof(groupId));
        }

        return new GroupMembership
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            CallerId = callerId,
            AddedAt = DateTimeOffset.UtcNow,
        };
    }
}
