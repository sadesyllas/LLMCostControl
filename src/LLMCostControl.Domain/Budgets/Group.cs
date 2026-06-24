namespace LLMCostControl.Domain.Budgets;

/// <summary>
/// A named group that callers can belong to. A group carries a budget for a
/// given period; callers inherit the largest budget among their groups.
/// </summary>
public class Group
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable group name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the group was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Creates a new <see cref="Group"/> with a fresh id and timestamp.
    /// </summary>
    /// <param name="name">The group name; cannot be empty.</param>
    public static Group Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Group name cannot be empty.", nameof(name));
        }

        return new Group
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
