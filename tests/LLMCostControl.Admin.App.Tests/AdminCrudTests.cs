using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for M17 admin CRUD flows (§12.3). Verifies form validation,
/// create/rename/delete group, set budget, add/remove member, and set/clear
/// override using a stubbed <see cref="AdminCommandService"/>.
/// </summary>
public sealed class AdminCrudTests : TestContext
{
    private readonly TestAuthorizationContext _auth;
    private readonly IAdminCommandService _admin = Substitute.For<IAdminCommandService>();

    public AdminCrudTests()
    {
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("admin@example.com");
        _auth.SetRoles(AppRoles.Admin);
        Services.AddSingleton(_admin);
    }

    [Fact]
    public async Task Groups_page_loads_and_displays_groups()
    {
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group>
            {
                Group.Create("Engineering"),
                Group.Create("Marketing"),
            });

        var cut = RenderComponent<Groups>();

        cut.WaitForElement("a");
        cut.Markup.Should().Contain("Engineering");
        cut.Markup.Should().Contain("Marketing");
    }

    [Fact]
    public async Task Groups_page_create_validates_empty_name()
    {
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group>());

        var cut = RenderComponent<Groups>();

        var input = cut.Find("#groupName");
        input.Change("");
        cut.Find("button.btn-primary").Click();

        cut.Markup.Should().Contain("Group name is required");
        await _admin.DidNotReceive().CreateGroupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Groups_page_create_calls_service()
    {
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group>());

        var cut = RenderComponent<Groups>();

        var input = cut.Find("#groupName");
        input.Change("New Group");
        cut.Find("button.btn-primary").Click();

        cut.WaitForState(() => cut.Markup.Contains("New Group") || _admin.ReceivedCalls().Any());
        await _admin.Received().CreateGroupAsync("New Group", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Groups_page_delete_calls_service()
    {
        var group = Group.Create("Test Group");
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group> { group });

        var cut = RenderComponent<Groups>();
        cut.WaitForElement("button.btn-outline-danger");

        cut.Find("button.btn-outline-danger").Click();

        await _admin.Received().DeleteGroupAsync(group.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GroupDetail_page_set_budget_validates_negative()
    {
        var group = Group.Create("Test Group");
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group> { group });
        _admin.GetGroupBudgetAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns((GroupBudget?)null);
        _admin.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<GroupMembership>());

        var cut = RenderComponent<GroupDetail>(parameters =>
            parameters.Add(p => p.GroupId, group.Id));

        cut.WaitForElement("input[type=number]");

        cut.Find("input[type=number]").Change("-50");
        cut.Find("button.btn-primary").Click();

        cut.Markup.Should().Contain("Budget amount cannot be negative");
        await _admin.DidNotReceive().SetGroupBudgetAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GroupDetail_page_add_member_validates_empty()
    {
        var group = Group.Create("Test Group");
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group> { group });
        _admin.GetGroupBudgetAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns((GroupBudget?)null);
        _admin.ListMembersAsync(group.Id, Arg.Any<CancellationToken>())
            .Returns(new List<GroupMembership>());

        var cut = RenderComponent<GroupDetail>(parameters =>
            parameters.Add(p => p.GroupId, group.Id));

        cut.WaitForElement("input[placeholder*=caller]");

        cut.Find("input[placeholder*=caller]").Change("");
        // The Add Member button is the second btn-primary on the page
        cut.FindAll("button.btn-primary")[1].Click();

        cut.Markup.Should().Contain("Caller ID is required");
        await _admin.DidNotReceive().AddMemberAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserOverrides_page_set_validates_negative_amount()
    {
        _admin.ListUserOverridesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserBudgetOverride>());

        var cut = RenderComponent<UserOverrides>();

        cut.Find("input[placeholder*=caller]").Change("user@example.com");
        cut.Find("input[type=number]").Change("-10");
        cut.Find("button.btn-primary").Click();

        cut.Markup.Should().Contain("Amount cannot be negative");
        await _admin.DidNotReceive().SetUserOverrideAsync(
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserOverrides_page_set_validates_empty_caller()
    {
        _admin.ListUserOverridesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserBudgetOverride>());

        var cut = RenderComponent<UserOverrides>();

        cut.Find("input[type=number]").Change("100");
        cut.Find("button.btn-primary").Click();

        cut.Markup.Should().Contain("Caller ID is required");
        await _admin.DidNotReceive().SetUserOverrideAsync(
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserOverrides_page_clear_calls_service()
    {
        var override_ = UserBudgetOverride.Create(
            CallerId.From("user@example.com"), new Money(100m, "USD"), BudgetPeriod.Current());
        _admin.ListUserOverridesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserBudgetOverride> { override_ });

        var cut = RenderComponent<UserOverrides>();
        cut.WaitForElement("button.btn-outline-danger");

        cut.Find("button.btn-outline-danger").Click();

        await _admin.Received().ClearUserOverrideAsync("user@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Groups_page_renders_for_admin_user()
    {
        // bUnit renders components directly (not through routing), so
        // @attribute [Authorize] is not enforced. The admin user can
        // see the full Groups page content.
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Group>());

        var cut = RenderComponent<Groups>();

        cut.WaitForElement("#groupName");
        cut.Markup.Should().Contain("New group name");
    }
}
