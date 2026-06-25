using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Integration tests for <see cref="AdminCommandService"/> against a real
/// Postgres container (§12.3). Verifies that all CRUD operations persist
/// correctly through the shared repositories.
/// </summary>
public sealed class AdminCommandIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IDbContextFactory<CostTrackerDbContext> _dbContextFactory = null!;
    private AdminCommandService _service = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var context = new CostTrackerDbContext(options);
        await context.Database.MigrateAsync();
        await context.DisposeAsync();

        var factory = new DbContextFactory(_postgres.GetConnectionString());
        _dbContextFactory = factory;
        var publisher = new NullPricingUpdatePublisher();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PricingFileImporter>.Instance;
        var importer = new PricingFileImporter(factory, publisher, logger);
        _service = new AdminCommandService(factory, importer);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task CreateGroup_persists_and_can_be_listed()
    {
        var group = await _service.CreateGroupAsync("Engineering");

        group.Id.Should().NotBeEmpty();
        group.Name.Should().Be("Engineering");

        var groups = await _service.ListGroupsAsync();
        groups.Should().Contain(g => g.Name == "Engineering");
    }

    [Fact]
    public async Task RenameGroup_updates_name()
    {
        var group = await _service.CreateGroupAsync("Old Name");

        await _service.RenameGroupAsync(group.Id, "New Name");

        var groups = await _service.ListGroupsAsync();
        var renamed = groups.First(g => g.Id == group.Id);
        renamed.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteGroup_removes_group()
    {
        var group = await _service.CreateGroupAsync("To Delete");

        await _service.DeleteGroupAsync(group.Id);

        var groups = await _service.ListGroupsAsync();
        groups.Should().NotContain(g => g.Id == group.Id);
    }

    [Fact]
    public async Task SetGroupBudget_persists_and_can_be_read()
    {
        var group = await _service.CreateGroupAsync("Budgeted Group");

        await _service.SetGroupBudgetAsync(group.Id, 500m, "USD");

        var budget = await _service.GetGroupBudgetAsync(group.Id);
        budget.Should().NotBeNull();
        budget!.Amount.Amount.Should().Be(500m);
        budget.Amount.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ClearGroupBudget_removes_budget()
    {
        var group = await _service.CreateGroupAsync("Clear Budget Group");
        await _service.SetGroupBudgetAsync(group.Id, 100m, "USD");

        await _service.ClearGroupBudgetAsync(group.Id);

        var budget = await _service.GetGroupBudgetAsync(group.Id);
        budget.Should().BeNull();
    }

    [Fact]
    public async Task AddMember_persists_and_can_be_listed()
    {
        var group = await _service.CreateGroupAsync("Member Group");

        await _service.AddMemberAsync(group.Id, "user@example.com");

        var members = await _service.ListMembersAsync(group.Id);
        members.Should().Contain(m => m.CallerId.Value == "user@example.com");
    }

    [Fact]
    public async Task RemoveMember_removes_membership()
    {
        var group = await _service.CreateGroupAsync("Remove Member Group");
        await _service.AddMemberAsync(group.Id, "user@example.com");

        await _service.RemoveMemberAsync(group.Id, "user@example.com");

        var members = await _service.ListMembersAsync(group.Id);
        members.Should().NotContain(m => m.CallerId.Value == "user@example.com");
    }

    [Fact]
    public async Task SetUserOverride_persists_and_can_be_listed()
    {
        await _service.SetUserOverrideAsync("override@example.com", 250m, "USD");

        var overrides = await _service.ListUserOverridesAsync();
        overrides.Should().Contain(o => o.CallerId.Value == "override@example.com");
    }

    [Fact]
    public async Task ClearUserOverride_removes_override()
    {
        await _service.SetUserOverrideAsync("clear@example.com", 100m, "USD");

        await _service.ClearUserOverrideAsync("clear@example.com");

        var overrides = await _service.ListUserOverridesAsync();
        overrides.Should().NotContain(o => o.CallerId.Value == "clear@example.com");
    }

    [Fact]
    public async Task Full_crud_flow_create_group_set_budget_add_member_set_override_delete()
    {
        // Create group
        var group = await _service.CreateGroupAsync("Flow Test Group");
        group.Name.Should().Be("Flow Test Group");

        // Set budget
        await _service.SetGroupBudgetAsync(group.Id, 1000m, "USD");
        var budget = await _service.GetGroupBudgetAsync(group.Id);
        budget!.Amount.Amount.Should().Be(1000m);

        // Add member
        await _service.AddMemberAsync(group.Id, "flow@example.com");
        var members = await _service.ListMembersAsync(group.Id);
        members.Should().HaveCount(1);
        members[0].CallerId.Value.Should().Be("flow@example.com");

        // Set override for the same user
        await _service.SetUserOverrideAsync("flow@example.com", 500m, "EUR");
        var overrides = await _service.ListUserOverridesAsync();
        overrides.Should().Contain(o => o.CallerId.Value == "flow@example.com");

        // Delete group
        await _service.DeleteGroupAsync(group.Id);
        var groups = await _service.ListGroupsAsync();
        groups.Should().NotContain(g => g.Id == group.Id);
    }

    /// <summary>
    /// Simple IDbContextFactory implementation for test use with a fixed
    /// connection string.
    /// </summary>
    private sealed class DbContextFactory : IDbContextFactory<CostTrackerDbContext>
    {
        private readonly DbContextOptions<CostTrackerDbContext> _options;

        public DbContextFactory(string connectionString)
        {
            _options = new DbContextOptionsBuilder<CostTrackerDbContext>()
                .UseNpgsql(connectionString)
                .Options;
        }

        public CostTrackerDbContext CreateDbContext()
        {
            return new CostTrackerDbContext(_options);
        }
    }
}
