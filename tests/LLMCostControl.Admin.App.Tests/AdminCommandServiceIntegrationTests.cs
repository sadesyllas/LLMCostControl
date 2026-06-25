using LLMCostControl.Admin.App.Services;
using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Integration tests for <see cref="AdminCommandService"/> against a real
/// Postgres (Testcontainers), exercising the shared-repository write path
/// (M17, §12.3). Requires Docker.
/// </summary>
public sealed class AdminCommandServiceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AdminCommandService _service = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var db = new CostTrackerDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _service = new AdminCommandService(new TestDbContextFactory(options));
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Minimal context factory backing the service with the test container's options.</summary>
    private sealed class TestDbContextFactory(DbContextOptions<CostTrackerDbContext> options)
        : IDbContextFactory<CostTrackerDbContext>
    {
        public CostTrackerDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Group_create_list_rename_delete_roundtrip()
    {
        var group = await _service.CreateGroupAsync("Engineering");

        (await _service.ListGroupsAsync()).Should().ContainSingle(g => g.Id == group.Id && g.Name == "Engineering");

        await _service.RenameGroupAsync(group.Id, "Platform");
        (await _service.ListGroupsAsync()).Should().Contain(g => g.Id == group.Id && g.Name == "Platform");

        await _service.DeleteGroupAsync(group.Id);
        (await _service.ListGroupsAsync()).Should().NotContain(g => g.Id == group.Id);
    }

    [Fact]
    public async Task Group_budget_set_update_and_clear()
    {
        var group = await _service.CreateGroupAsync("Budgeted");

        await _service.SetGroupBudgetAsync(group.Id, 100m, "USD");
        var budget = await _service.GetGroupBudgetAsync(group.Id);
        budget.Should().NotBeNull();
        budget!.Amount.Amount.Should().Be(100m);
        budget.Amount.Currency.Should().Be("USD");

        await _service.SetGroupBudgetAsync(group.Id, 250m, "USD");
        (await _service.GetGroupBudgetAsync(group.Id))!.Amount.Amount.Should().Be(250m);

        await _service.ClearGroupBudgetAsync(group.Id);
        (await _service.GetGroupBudgetAsync(group.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Membership_add_list_duplicate_and_remove()
    {
        var group = await _service.CreateGroupAsync("Members");

        await _service.AddMemberAsync(group.Id, "alice@example.com");
        (await _service.ListMembersAsync(group.Id)).Should().ContainSingle().Which.Should().Be("alice@example.com");

        var duplicate = async () => await _service.AddMemberAsync(group.Id, "alice@example.com");
        await duplicate.Should().ThrowAsync<InvalidOperationException>();

        await _service.RemoveMemberAsync(group.Id, "alice@example.com");
        (await _service.ListMembersAsync(group.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task User_override_set_update_and_clear()
    {
        await _service.SetUserOverrideAsync("override-user@example.com", 75m, "USD");
        var ovr = await _service.GetUserOverrideAsync("override-user@example.com");
        ovr.Should().NotBeNull();
        ovr!.Amount.Amount.Should().Be(75m);

        await _service.SetUserOverrideAsync("override-user@example.com", 120m, "USD");
        (await _service.GetUserOverrideAsync("override-user@example.com"))!.Amount.Amount.Should().Be(120m);

        await _service.ClearUserOverrideAsync("override-user@example.com");
        (await _service.GetUserOverrideAsync("override-user@example.com")).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_group_budget_is_rejected(int amount)
    {
        var group = await _service.CreateGroupAsync("Validation");

        var act = async () => await _service.SetGroupBudgetAsync(group.Id, amount, "USD");

        await act.Should().ThrowAsync<ArgumentException>();
        (await _service.GetGroupBudgetAsync(group.Id)).Should().BeNull();
    }
}
