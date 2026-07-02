using System;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using FluentAssertions;
using Xunit;

namespace LLMCostControl.Domain.Tests;

public class EffectiveBudgetTests
{
    [Fact]
    public void Resolve_returns_user_override_when_present()
    {
        var callerId = CallerId.From("alice@example.com");
        var override_ = UserBudgetOverride.Create(callerId, new Money(100m, "USD"), BudgetPeriodType.Monthly);
        var groupBudgets = new[]
        {
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(50m, "USD"), BudgetPeriodType.Monthly)),
        };

        var result = EffectiveBudget.Resolve(override_, groupBudgets, BudgetPeriodType.Monthly);

        result.Source.Should().Be(BudgetSource.UserOverride);
        result.Amount.Should().Be(new Money(100m, "USD"));
        result.GroupId.Should().BeNull();
        result.PeriodType.Should().Be(BudgetPeriodType.Monthly);
    }

    [Fact]
    public void Resolve_returns_largest_group_budget_when_no_override()
    {
        var smallGroupId = Guid.NewGuid();
        var bigGroupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (smallGroupId, GroupBudget.Create(smallGroupId, new Money(30m, "USD"), BudgetPeriodType.Monthly)),
            (bigGroupId, GroupBudget.Create(bigGroupId, new Money(75m, "USD"), BudgetPeriodType.Monthly)),
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(50m, "USD"), BudgetPeriodType.Monthly)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets, BudgetPeriodType.Monthly);

        result.Source.Should().Be(BudgetSource.Group);
        result.Amount.Should().Be(new Money(75m, "USD"));
        result.GroupId.Should().Be(bigGroupId);
        result.PeriodType.Should().Be(BudgetPeriodType.Monthly);
    }

    [Fact]
    public void Resolve_returns_none_when_no_override_and_no_groups()
    {
        var result = EffectiveBudget.Resolve(null, Array.Empty<(Guid, GroupBudget)>(), BudgetPeriodType.Monthly);

        result.Source.Should().Be(BudgetSource.None);
        result.Amount.Should().BeNull();
        result.HasBudget.Should().BeFalse();
        result.PeriodType.Should().Be(BudgetPeriodType.Monthly);
    }

    [Fact]
    public void HasBudget_true_when_group_budget_is_nonzero()
    {
        var groupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (groupId, GroupBudget.Create(groupId, new Money(50m, "USD"), BudgetPeriodType.Monthly)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets, BudgetPeriodType.Monthly);

        result.HasBudget.Should().BeTrue();
    }

    [Fact]
    public void HasBudget_true_when_group_budget_is_zero_so_zero_is_a_real_budget()
    {
        // A zero budget is an explicit "spend nothing" budget (a cut-off), NOT the
        // same as having no budget — so HasBudget is true and the check denies
        // (remaining 0) regardless of AllowNonBudgetedUsers (§7).
        var groupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (groupId, GroupBudget.Create(groupId, Money.Zero("USD"), BudgetPeriodType.Monthly)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets, BudgetPeriodType.Monthly);

        result.HasBudget.Should().BeTrue();
        result.Amount!.IsZero.Should().BeTrue();
        result.Source.Should().Be(BudgetSource.Group);
    }

    [Fact]
    public void Resolve_returns_weekly_override_when_present()
    {
        var callerId = CallerId.From("alice@example.com");
        var override_ = UserBudgetOverride.Create(callerId, new Money(20m, "USD"), BudgetPeriodType.Weekly);
        var groupBudgets = new[]
        {
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(10m, "USD"), BudgetPeriodType.Weekly)),
        };

        var result = EffectiveBudget.Resolve(override_, groupBudgets, BudgetPeriodType.Weekly);

        result.Source.Should().Be(BudgetSource.UserOverride);
        result.Amount.Should().Be(new Money(20m, "USD"));
        result.GroupId.Should().BeNull();
        result.PeriodType.Should().Be(BudgetPeriodType.Weekly);
    }

    [Fact]
    public void Resolve_returns_largest_weekly_group_budget_when_no_override()
    {
        var smallGroupId = Guid.NewGuid();
        var bigGroupId = Guid.NewGuid();
        var groupBudgets = new[]
        {
            (smallGroupId, GroupBudget.Create(smallGroupId, new Money(5m, "USD"), BudgetPeriodType.Weekly)),
            (bigGroupId, GroupBudget.Create(bigGroupId, new Money(15m, "USD"), BudgetPeriodType.Weekly)),
            (Guid.NewGuid(), GroupBudget.Create(Guid.NewGuid(), new Money(8m, "USD"), BudgetPeriodType.Weekly)),
        };

        var result = EffectiveBudget.Resolve(null, groupBudgets, BudgetPeriodType.Weekly);

        result.Source.Should().Be(BudgetSource.Group);
        result.Amount.Should().Be(new Money(15m, "USD"));
        result.GroupId.Should().Be(bigGroupId);
        result.PeriodType.Should().Be(BudgetPeriodType.Weekly);
    }
}
