using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LLMCostControl.Admin.App.Tests.Integration;

/// <summary>
/// Integration tests for M17 admin command handlers
/// (<see cref="GroupAdminService"/>) against a real PostgreSQL instance
/// managed by Testcontainers (§12.3).
/// <para>
/// Run via WSL with <c>TESTCONTAINERS_RYUK_DISABLED=true</c>; run on the
/// Windows host for the pure-EF portion only.
/// </para>
/// </summary>
[Collection("AdminCommandHandlerIntegration")]
public sealed class AdminCommandHandlerTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;
    private CostTrackerDbContext _db = null!;
    private GroupAdminService _service = null!;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _pg.StartAsync();

        var options = new DbContextOptionsBuilder<CostTrackerDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        _db = new CostTrackerDbContext(options);
        await _db.Database.MigrateAsync();

        _service = new GroupAdminService(
            new GroupRepository(_db),
            new GroupMembershipRepository(_db),
            new GroupBudgetRepository(_db),
            new UserBudgetOverrideRepository(_db));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task FullCrudFlow_group_budget_membership_override()
    {
        var period = BudgetPeriod.FromDate(DateTimeOffset.UtcNow);

        // ── Create group ──────────────────────────────────────────────────────
        var group = await _service.CreateGroupAsync("IntegrationTestGroup");
        group.Name.Should().Be("IntegrationTestGroup");

        var groups = await _service.GetGroupsAsync();
        groups.Should().Contain(g => g.Id == group.Id);

        // ── Rename group ──────────────────────────────────────────────────────
        await _service.RenameGroupAsync(group.Id, "RenamedGroup");
        var renamed = (await _service.GetGroupsAsync()).First(g => g.Id == group.Id);
        renamed.Name.Should().Be("RenamedGroup");

        // ── Set budget ────────────────────────────────────────────────────────
        await _service.SetGroupBudgetAsync(group.Id, 100m, "USD", period);
        var budget = await _service.GetGroupBudgetAsync(group.Id, period);
        budget.Should().NotBeNull();
        budget!.Amount.Amount.Should().Be(100m);
        budget.Amount.Currency.Should().Be("USD");

        // ── Clear budget ──────────────────────────────────────────────────────
        await _service.ClearGroupBudgetAsync(group.Id, period);
        var clearedBudget = await _service.GetGroupBudgetAsync(group.Id, period);
        clearedBudget.Should().BeNull();

        // ── Add member ────────────────────────────────────────────────────────
        await _service.AddMemberAsync(group.Id, "user@example.com");
        var members = await _service.GetGroupMembersAsync(group.Id);
        members.Should().Contain(m => m.CallerId.Value == "user@example.com");

        // ── Duplicate member is silently ignored ──────────────────────────────
        await _service.AddMemberAsync(group.Id, "user@example.com");
        var membersAfterDuplicate = await _service.GetGroupMembersAsync(group.Id);
        membersAfterDuplicate.Count(m => m.CallerId.Value == "user@example.com")
            .Should().Be(1, "duplicate membership must not be created");

        // ── Remove member ─────────────────────────────────────────────────────
        await _service.RemoveMemberAsync(group.Id, "user@example.com");
        var membersAfterRemove = await _service.GetGroupMembersAsync(group.Id);
        membersAfterRemove.Should().NotContain(m => m.CallerId.Value == "user@example.com");

        // ── Set user override ─────────────────────────────────────────────────
        await _service.SetUserOverrideAsync("user@example.com", 200m, "USD", period);
        var override_ = await _service.GetUserOverrideAsync("user@example.com", period);
        override_.Should().NotBeNull();
        override_!.Amount.Amount.Should().Be(200m);

        // ── Clear user override ───────────────────────────────────────────────
        await _service.ClearUserOverrideAsync("user@example.com", period);
        var clearedOverride = await _service.GetUserOverrideAsync("user@example.com", period);
        clearedOverride.Should().BeNull();

        // ── Delete group ──────────────────────────────────────────────────────
        await _service.DeleteGroupAsync(group.Id);
        var groupsAfter = await _service.GetGroupsAsync();
        groupsAfter.Should().NotContain(g => g.Id == group.Id);
    }

    [Fact]
    public async Task SetGroupBudget_validates_zero_amount()
    {
        var group = await _service.CreateGroupAsync("BudgetValidation");
        var period = BudgetPeriod.FromDate(DateTimeOffset.UtcNow);

        var act = async () => await _service.SetGroupBudgetAsync(group.Id, 0m, "USD", period);
        await act.Should().ThrowAsync<ArgumentException>("zero budget is invalid");
    }

    [Fact]
    public async Task SetGroupBudget_validates_negative_amount()
    {
        var group = await _service.CreateGroupAsync("NegBudgetValidation");
        var period = BudgetPeriod.FromDate(DateTimeOffset.UtcNow);

        var act = async () => await _service.SetGroupBudgetAsync(group.Id, -5m, "USD", period);
        await act.Should().ThrowAsync<ArgumentException>("negative budget is invalid");
    }

    [Fact]
    public async Task AddMember_validates_empty_caller_id()
    {
        var group = await _service.CreateGroupAsync("MemberValidation");

        var act = async () => await _service.AddMemberAsync(group.Id, "");
        await act.Should().ThrowAsync<ArgumentException>("empty caller id is invalid");
    }

    [Fact]
    public async Task SetUserOverride_validates_non_positive_amount()
    {
        var period = BudgetPeriod.FromDate(DateTimeOffset.UtcNow);
        var act = async () => await _service.SetUserOverrideAsync("test@example.com", 0m, "USD", period);
        await act.Should().ThrowAsync<ArgumentException>("zero override amount is invalid");
    }
}

[CollectionDefinition("AdminCommandHandlerIntegration")]
public class AdminCommandHandlerIntegrationCollection { }
