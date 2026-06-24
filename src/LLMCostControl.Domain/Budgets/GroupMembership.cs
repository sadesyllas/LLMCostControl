using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Budgets;

public class GroupMembership
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public CallerId CallerId { get; init; }
    public DateTimeOffset AddedAt { get; init; }

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
