namespace LLMCostControl.Domain.Budgets;

public class Group
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

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
