using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Tests;

public class BudgetResolutionTests : RepositoryTestBase
{
    [Fact]
    public async Task Resolve_returns_none_when_no_membership_and_no_override()
    {
        var repo = new BudgetResolutionRepository(Db);
        var caller = CallerId.From("nobody@example.com");
        var period = new BudgetPeriod(2026, 6);

        var result = await repo.ResolveAsync(caller, period);

        result.Source.Should().Be(BudgetSource.None);
        result.HasBudget.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_returns_user_override_when_present()
    {
        var caller = CallerId.From("alice@example.com");
        var period = new BudgetPeriod(2026, 6);
        var group = Group.Create("Eng");
        await new GroupRepository(Db).AddAsync(group);

        var membership = GroupMembership.Create(group.Id, caller);
        await new GroupMembershipRepository(Db).AddAsync(membership);

        var groupBudget = GroupBudget.Create(group.Id, new Money(50m, "USD"), period);
        await new GroupBudgetRepository(Db).UpsertAsync(groupBudget);

        var overrideEntity = UserBudgetOverride.Create(caller, new Money(100m, "USD"), period);
        await new UserBudgetOverrideRepository(Db).UpsertAsync(overrideEntity);

        var repo = new BudgetResolutionRepository(Db);
        var result = await repo.ResolveAsync(caller, period);

        result.Source.Should().Be(BudgetSource.UserOverride);
        result.Amount.Should().Be(new Money(100m, "USD"));
        result.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_returns_largest_group_budget_when_no_override()
    {
        var caller = CallerId.From("bob@example.com");
        var period = new BudgetPeriod(2026, 6);

        var smallGroup = Group.Create("Small");
        var bigGroup = Group.Create("Big");
        var groupRepo = new GroupRepository(Db);
        await groupRepo.AddAsync(smallGroup);
        await groupRepo.AddAsync(bigGroup);

        var membershipRepo = new GroupMembershipRepository(Db);
        await membershipRepo.AddAsync(GroupMembership.Create(smallGroup.Id, caller));
        await membershipRepo.AddAsync(GroupMembership.Create(bigGroup.Id, caller));

        var budgetRepo = new GroupBudgetRepository(Db);
        await budgetRepo.UpsertAsync(GroupBudget.Create(smallGroup.Id, new Money(30m, "USD"), period));
        await budgetRepo.UpsertAsync(GroupBudget.Create(bigGroup.Id, new Money(75m, "USD"), period));

        var repo = new BudgetResolutionRepository(Db);
        var result = await repo.ResolveAsync(caller, period);

        result.Source.Should().Be(BudgetSource.Group);
        result.Amount.Should().Be(new Money(75m, "USD"));
        result.GroupId.Should().Be(bigGroup.Id);
    }

    [Fact]
    public async Task Resolve_returns_none_when_member_but_no_budget_for_period()
    {
        var caller = CallerId.From("carol@example.com");
        var period = new BudgetPeriod(2026, 6);
        var group = Group.Create("NoBudget");
        await new GroupRepository(Db).AddAsync(group);
        await new GroupMembershipRepository(Db).AddAsync(GroupMembership.Create(group.Id, caller));

        var repo = new BudgetResolutionRepository(Db);
        var result = await repo.ResolveAsync(caller, period);

        result.Source.Should().Be(BudgetSource.None);
        result.HasBudget.Should().BeFalse();
    }
}
