using Bunit;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Admin.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the Administration page (M17, §12.3): the full CRUD flow
/// (create group → set budget → add member → set override → delete) and form
/// validation (negative/zero budget, empty caller id, duplicate membership).
/// </summary>
public sealed class AdministrationPageTests : BunitContext
{
    private IRenderedComponent<Administration> RenderPage()
    {
        Services.AddSingleton<IAdminCommandService>(new FakeAdminCommandService());
        return Render<Administration>();
    }

    [Fact]
    public void Full_crud_flow_create_set_budget_add_member_set_override_delete()
    {
        var cut = RenderPage();

        // Create group → it appears and is auto-selected.
        cut.Find("[data-testid=new-group-name]").Change("Engineering");
        cut.Find("[data-testid=create-group]").Click();
        cut.FindAll("[data-testid=group-row]").Should().ContainSingle();
        cut.Find("[data-testid=selected-group-name]").TextContent.Should().Be("Engineering");

        // Set the group budget for the current period.
        cut.Find("[data-testid=budget-amount]").Change("100");
        cut.Find("[data-testid=budget-currency]").Change("USD");
        cut.Find("[data-testid=set-budget]").Click();
        cut.Find("[data-testid=current-budget]").TextContent.Should().Contain("100").And.Contain("USD");

        // Add a member.
        cut.Find("[data-testid=new-member]").Change("alice@example.com");
        cut.Find("[data-testid=add-member]").Click();
        cut.FindAll("[data-testid=member-id]").Should().ContainSingle()
            .Which.TextContent.Should().Be("alice@example.com");

        // Set a per-user override.
        cut.Find("[data-testid=override-caller]").Change("bob@example.com");
        cut.Find("[data-testid=override-amount]").Change("50");
        cut.Find("[data-testid=override-currency]").Change("USD");
        cut.Find("[data-testid=set-override]").Click();
        cut.Find("[data-testid=current-override]").TextContent.Should().Contain("50");

        // Delete the group → list empties and the selected section disappears.
        cut.Find("[data-testid=delete-group]").Click();
        cut.FindAll("[data-testid=group-row]").Should().BeEmpty();
        cut.FindAll("[data-testid=selected-group]").Should().BeEmpty();
    }

    [Fact]
    public void Creating_a_group_with_an_empty_name_shows_a_validation_error()
    {
        var cut = RenderPage();

        cut.Find("[data-testid=create-group]").Click();

        cut.Find("[data-testid=error]").TextContent.Should().Contain("empty");
        cut.FindAll("[data-testid=group-row]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Setting_a_non_positive_budget_shows_a_validation_error(string amount)
    {
        var cut = RenderPage();
        cut.Find("[data-testid=new-group-name]").Change("Ops");
        cut.Find("[data-testid=create-group]").Click();

        cut.Find("[data-testid=budget-amount]").Change(amount);
        cut.Find("[data-testid=set-budget]").Click();

        cut.Find("[data-testid=error]").TextContent.Should().Contain("greater than zero");
        cut.Find("[data-testid=current-budget]").TextContent.Should().Be("none");
    }

    [Fact]
    public void Adding_an_empty_caller_id_shows_a_validation_error()
    {
        var cut = RenderPage();
        cut.Find("[data-testid=new-group-name]").Change("Ops");
        cut.Find("[data-testid=create-group]").Click();

        cut.Find("[data-testid=add-member]").Click();

        cut.Find("[data-testid=error]").TextContent.Should().Contain("empty");
        cut.FindAll("[data-testid=member-id]").Should().BeEmpty();
    }

    [Fact]
    public void Adding_a_duplicate_member_shows_a_validation_error()
    {
        var cut = RenderPage();
        cut.Find("[data-testid=new-group-name]").Change("Ops");
        cut.Find("[data-testid=create-group]").Click();

        cut.Find("[data-testid=new-member]").Change("alice@example.com");
        cut.Find("[data-testid=add-member]").Click();
        cut.Find("[data-testid=new-member]").Change("alice@example.com");
        cut.Find("[data-testid=add-member]").Click();

        cut.Find("[data-testid=error]").TextContent.Should().Contain("already a member");
        cut.FindAll("[data-testid=member-id]").Should().ContainSingle();
    }
}
