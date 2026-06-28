using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Tests;

public class EffectiveBudgetTests
{
    [Fact]
    public void Resolve_returns_user_override_when_present()
    {
        var callerId = CallerId.From("alice@example.com");
        var period = new BudgetPeriod(2026, 6);
        var override_ = UserBudgetOverride.Create(callerId, new Money(100m, "USD"), period);
        var groupBudgets = new[]
        {
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(50m, "USD"), period)),
        };

        var result = EffectiveBudget.Resolve(override_, groupBudgets);

        result.Source.Should().Be(BudgetSource.UserOverride);
        result.Amount.Should().Be(new Money(100m, "USD"));
        result.GroupId.Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_largest_group_budget_when_no_override()
    {
        var period = new BudgetPeriod(2026, 6);
        var smallGroupId = Guid.NewGuid();
        var bigGroupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (smallGroupId, GroupBudget.Create(smallGroupId, new Money(30m, "USD"), period)),
            (bigGroupId, GroupBudget.Create(bigGroupId, new Money(75m, "USD"), period)),
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(50m, "USD"), period)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets);

        result.Source.Should().Be(BudgetSource.Group);
        result.Amount.Should().Be(new Money(75m, "USD"));
        result.GroupId.Should().Be(bigGroupId);
    }

    [Fact]
    public void Resolve_returns_none_when_no_override_and_no_groups()
    {
        var result = EffectiveBudget.Resolve(null, Array.Empty<(Guid, GroupBudget)>());

        result.Source.Should().Be(BudgetSource.None);
        result.Amount.Should().BeNull();
        result.HasBudget.Should().BeFalse();
    }

    [Fact]
    public void HasBudget_true_when_group_budget_is_nonzero()
    {
        var period = new BudgetPeriod(2026, 6);
        var groupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (groupId, GroupBudget.Create(groupId, new Money(50m, "USD"), period)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets);

        result.HasBudget.Should().BeTrue();
    }

    [Fact]
    public void HasBudget_true_when_group_budget_is_zero_so_zero_is_a_real_budget()
    {
        // A zero budget is an explicit "spend nothing" budget (a cut-off), NOT the
        // same as having no budget — so HasBudget is true and the check denies
        // (remaining 0) regardless of AllowNonBudgetedUsers (§7).
        var period = new BudgetPeriod(2026, 6);
        var groupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (groupId, GroupBudget.Create(groupId, Money.Zero("USD"), period)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets);

        result.HasBudget.Should().BeTrue();
        result.Amount!.IsZero.Should().BeTrue();
        result.Source.Should().Be(BudgetSource.Group);
    }
}
