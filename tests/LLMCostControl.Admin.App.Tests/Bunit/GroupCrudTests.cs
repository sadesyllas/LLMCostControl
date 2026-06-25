using Bunit;
using LLMCostControl.Admin.App.Components.Pages.Admin;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LLMCostControl.Admin.App.Tests.Bunit;

/// <summary>
/// bUnit tests for M17 Groups admin page: CRUD flows and form validation
/// (§12.3). Uses NSubstitute to mock <see cref="IGroupAdminService"/> so no
/// database is required.
/// </summary>
public sealed class GroupCrudTests : BunitContext
{
    private readonly IGroupAdminService _service;

    public GroupCrudTests()
    {
        _service = Substitute.For<IGroupAdminService>();

        // Default: return empty group list.
        _service.GetGroupsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Group>>([]));

        Services.AddSingleton(_service);
        Services.AddAuthorizationCore();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Groups_page_renders_heading()
    {
        var cut = Render<Groups>();
        cut.Find("#groups-heading").TextContent.Should().Contain("Groups");
    }

    [Fact]
    public void Groups_page_renders_group_list()
    {
        _service.GetGroupsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Group>>(
                    [Group.Create("Alpha"), Group.Create("Beta")]));

        var cut = Render<Groups>();

        cut.Find("#groups-table").TextContent.Should().Contain("Alpha");
        cut.Find("#groups-table").TextContent.Should().Contain("Beta");
    }

    [Fact]
    public void Create_group_validates_empty_name()
    {
        var cut = Render<Groups>();

        // Click Create without filling the name input.
        cut.Find("#create-group-btn").Click();

        // Validation error should appear.
        cut.Find("#create-group-error").TextContent.Should().Contain("required");
    }

    [Fact]
    public async Task Create_group_calls_service_with_trimmed_name()
    {
        _service.CreateGroupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(Group.Create((string)ci[0])));

        var cut = Render<Groups>();
        cut.Find("#new-group-name").Change("  My Group  ");
        cut.Find("#create-group-btn").Click();

        await cut.InvokeAsync(async () => await Task.CompletedTask);

        await _service.Received(1).CreateGroupAsync("My Group", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_group_calls_service()
    {
        var groupId = Guid.NewGuid();
        var group = new Group { Id = groupId, Name = "ToDelete" };
        _service.GetGroupsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Group>>([group]));

        var cut = Render<Groups>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.Find($"#delete-btn-{groupId}").Click();

        await _service.Received().DeleteGroupAsync(groupId, Arg.Any<CancellationToken>());
    }
}
