using System;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the Groups CRUD capabilities (§12.3, M17).
/// Exercises creating, renaming, deleting, budgeting, and member additions/removals.
/// </summary>
public sealed class GroupCrudTests : TestContext, IDisposable
{
    private readonly SqliteTestDbContextFactory _dbFactory;

    public GroupCrudTests()
    {
        _dbFactory = new SqliteTestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<CostTrackerDbContext>>(_dbFactory);
    }

    /// <summary>Disposes of the SQLite in-memory database connection.</summary>
    public new void Dispose()
    {
        _dbFactory.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task AdminUser_CanPerformFullGroupCrudWorkflow()
    {
        // 1. Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // 2. Render Page
        var cut = RenderComponent<Groups>();

        // 3. Create Group
        cut.Find("#newGroupName").Change("engineering");
        await cut.InvokeAsync(() => cut.Find("#btn-create-group").Click());

        // Assert created in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var groups = await db.Groups.ToListAsync();
            groups.Should().ContainSingle(g => g.Name == "engineering");
        }

        // Assert rendered
        cut.Markup.Should().Contain("engineering");

        // Get group ID from DB to query card
        Guid groupId;
        using (var db = _dbFactory.CreateDbContext())
        {
            groupId = (await db.Groups.FirstAsync(g => g.Name == "engineering")).Id;
        }
        var groupCard = cut.Find($"#group-{groupId}");
        groupCard.Should().NotBeNull();

        // 4. Set Group Budget
        var amountInput = cut.Find($"#group-{groupId} .input-budget-amount");
        var currencySelect = cut.Find($"#group-{groupId} .select-budget-currency");
        var setBudgetBtn = cut.Find($"#group-{groupId} .btn-set-budget");

        amountInput.Change("500.00");
        currencySelect.Change("USD");
        await cut.InvokeAsync(() => setBudgetBtn.Click());

        // Assert budget saved
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == groupId);
            budget.Should().NotBeNull();
            budget!.Amount.Amount.Should().Be(500.00m);
            budget.Amount.Currency.Should().Be("USD");
        }
        cut.Find($"#group-{groupId} .budget-amount-display").TextContent.Should().Contain("500.00");

        // 5. Add Member to Group
        var memberInput = cut.Find($"#group-{groupId} .input-member-email");
        var addMemberBtn = cut.Find($"#group-{groupId} .btn-add-member");

        memberInput.Change("developer@example.com");
        await cut.InvokeAsync(() => addMemberBtn.Click());

        // Assert member saved
        using (var db = _dbFactory.CreateDbContext())
        {
            var members = await db.GroupMemberships.Where(m => m.GroupId == groupId).ToListAsync();
            members.Should().ContainSingle(m => m.CallerId.Value == "developer@example.com");
        }
        cut.Find($"#group-{groupId} .members-list").TextContent.Should().Contain("developer@example.com");

        // 6. Rename Group
        var renameBtn = cut.Find($"#group-{groupId} .btn-rename-group");
        await cut.InvokeAsync(() => renameBtn.Click());

        var nameInput = cut.Find($"#group-{groupId} .edit-group-name-input");
        var saveRenameBtn = cut.Find($"#group-{groupId} .btn-save-rename");

        nameInput.Change("engineering-team");
        await cut.InvokeAsync(() => saveRenameBtn.Click());

        // Assert rename saved
        using (var db = _dbFactory.CreateDbContext())
        {
            var group = await db.Groups.FindAsync(groupId);
            group!.Name.Should().Be("engineering-team");
        }
        cut.Markup.Should().Contain("engineering-team");

        // 7. Remove Member
        var removeMemberBtn = cut.Find($"#group-{groupId} .btn-remove-member");
        await cut.InvokeAsync(() => removeMemberBtn.Click());

        // Assert member removed
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyMember = await db.GroupMemberships.AnyAsync(m => m.GroupId == groupId);
            anyMember.Should().BeFalse();
        }
        cut.Find($"#group-{groupId}").TextContent.Should().Contain("No members assigned to this group.");

        // 8. Clear Budget
        var clearBudgetBtn = cut.Find($"#group-{groupId} .btn-clear-budget");
        await cut.InvokeAsync(() => clearBudgetBtn.Click());

        // Assert budget cleared
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyBudget = await db.GroupBudgets.AnyAsync(b => b.GroupId == groupId);
            anyBudget.Should().BeFalse();
        }
        cut.Find($"#group-{groupId} .budget-amount-display").TextContent.Should().Contain("No budget set for current period.");

        // 9. Delete Group
        var deleteGroupBtn = cut.Find($"#group-{groupId} .btn-delete-group");
        await cut.InvokeAsync(() => deleteGroupBtn.Click());

        // Assert group deleted
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyGroup = await db.Groups.AnyAsync(g => g.Id == groupId);
            anyGroup.Should().BeFalse();
        }
        cut.FindAll($"#group-{groupId}").Should().BeEmpty();
    }

    [Fact]
    public async Task ReadOnlyUser_CannotPerformGroupModifications()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("reader@example.com");
        authContext.SetRoles("CostTracker.ReadOnly");

        // Seed a group in DB
        var group = Group.Create("marketing");
        using (var db = _dbFactory.CreateDbContext())
        {
            await db.Groups.AddAsync(group);
            await db.SaveChangesAsync();
        }

        // Act
        var cut = RenderComponent<Groups>();

        // Assert
        cut.Markup.Should().Contain("marketing");
        
        // Admin create elements should be hidden
        cut.FindAll("#newGroupName").Should().BeEmpty();
        cut.FindAll("#btn-create-group").Should().BeEmpty();

        // Admin action buttons on group card should be hidden
        cut.FindAll($".btn-rename-group").Should().BeEmpty();
        cut.FindAll($".btn-delete-group").Should().BeEmpty();
        cut.FindAll($".input-budget-amount").Should().BeEmpty();
        cut.FindAll($".btn-set-budget").Should().BeEmpty();
        cut.FindAll($".input-member-email").Should().BeEmpty();
        cut.FindAll($".btn-add-member").Should().BeEmpty();
        cut.FindAll($".btn-remove-member").Should().BeEmpty();
    }
}
