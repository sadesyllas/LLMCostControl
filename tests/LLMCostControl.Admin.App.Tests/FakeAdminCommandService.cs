using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// In-memory <see cref="IAdminCommandService"/> for bUnit component tests. Mirrors
/// the real service's validation (domain factory guards + positive-amount +
/// duplicate-membership checks) so form-validation behaviour can be asserted
/// without a database.
/// </summary>
public sealed class FakeAdminCommandService : IAdminCommandService
{
    private readonly List<Group> _groups = [];
    private readonly Dictionary<Guid, GroupBudget> _budgets = [];
    private readonly Dictionary<Guid, List<string>> _members = [];
    private readonly Dictionary<string, UserBudgetOverride> _overrides = [];

    public Task<IReadOnlyList<Group>> ListGroupsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Group>>(_groups.ToList());

    public Task<Group> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        var group = Group.Create(name);
        _groups.Add(group);
        return Task.FromResult(group);
    }

    public Task RenameGroupAsync(Guid groupId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Group name cannot be empty.", nameof(newName));
        }

        _groups.Single(g => g.Id == groupId).Name = newName.Trim();
        return Task.CompletedTask;
    }

    public Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        _groups.RemoveAll(g => g.Id == groupId);
        _budgets.Remove(groupId);
        _members.Remove(groupId);
        return Task.CompletedTask;
    }

    public Task<GroupBudget?> GetGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
        => Task.FromResult(_budgets.GetValueOrDefault(groupId));

    public Task SetGroupBudgetAsync(Guid groupId, decimal amount, string currency, CancellationToken ct = default)
    {
        _budgets[groupId] = GroupBudget.Create(groupId, BuildPositiveMoney(amount, currency), BudgetPeriod.Current());
        return Task.CompletedTask;
    }

    public Task ClearGroupBudgetAsync(Guid groupId, CancellationToken ct = default)
    {
        _budgets.Remove(groupId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListMembersAsync(Guid groupId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_members.TryGetValue(groupId, out var list) ? list.ToList() : []);

    public Task AddMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        var members = _members.TryGetValue(groupId, out var list) ? list : _members[groupId] = [];

        if (members.Contains(caller.Value))
        {
            throw new InvalidOperationException($"Caller '{caller}' is already a member of this group.");
        }

        members.Add(caller.Value);
        return Task.CompletedTask;
    }

    public Task RemoveMemberAsync(Guid groupId, string callerId, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        if (_members.TryGetValue(groupId, out var list))
        {
            list.Remove(caller.Value);
        }

        return Task.CompletedTask;
    }

    public Task<UserBudgetOverride?> GetUserOverrideAsync(string callerId, CancellationToken ct = default)
        => Task.FromResult(_overrides.GetValueOrDefault(CallerId.From(callerId).Value));

    public Task SetUserOverrideAsync(string callerId, decimal amount, string currency, CancellationToken ct = default)
    {
        var caller = CallerId.From(callerId);
        _overrides[caller.Value] = UserBudgetOverride.Create(caller, BuildPositiveMoney(amount, currency), BudgetPeriod.Current());
        return Task.CompletedTask;
    }

    public Task ClearUserOverrideAsync(string callerId, CancellationToken ct = default)
    {
        _overrides.Remove(CallerId.From(callerId).Value);
        return Task.CompletedTask;
    }

    private static Money BuildPositiveMoney(decimal amount, string currency)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("Budget amount must be greater than zero.", nameof(amount));
        }

        return new Money(amount, currency);
    }
}
