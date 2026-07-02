using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the Groups CRUD capabilities (§12.3, M17).
/// Exercises creating, renaming, deleting, budgeting, and member additions/removals.
/// </summary>
[Collection("PostgresCollection")]
public sealed class GroupCrudTests : TestContext, IDisposable
{
    private readonly PostgresTestDbContextFactory _dbFactory;

    public GroupCrudTests(PostgresFixture fixture)
    {
        _dbFactory = new PostgresTestDbContextFactory(fixture.ConnectionString);
        _dbFactory.ResetDatabase();
        Services.AddSingleton<IDbContextFactory<CostTrackerDbContext>>(_dbFactory);
    }

    /// <summary>Disposes of the test services.</summary>
    public new void Dispose()
    {
        base.Dispose();
    }

    [Fact]
    public async Task AdminUser_CanPerformFullGroupCrudWorkflow()
    {
        // 1. Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // 2. Render Page & Wait for Load
        var cut = RenderComponent<Groups>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // 3. Create Group
        cut.Find("#newGroupName").Change("engineering");
        await cut.InvokeAsync(() => cut.Find("#btn-create-group").Click());

        // Assert created in DB
        Guid groupId = Guid.Empty;
        cut.WaitForAssertion(() =>
        {
            using var db = _dbFactory.CreateDbContext();
            var group = db.Groups.FirstOrDefault(g => g.Name == "engineering");
            group.Should().NotBeNull();
            groupId = group!.Id;
        });

        // Assert rendered
        var groupCard = cut.WaitForElement($"#group-{groupId}");
        groupCard.Should().NotBeNull();

        // 4. Set Group Monthly Budget
        var amountInput = cut.Find($"#group-{groupId} .input-budget-amount");
        var currencySelect = cut.Find($"#group-{groupId} .select-budget-currency");
        var setBudgetBtn = cut.Find($"#group-{groupId} .btn-set-budget");

        amountInput.Change("500.00");
        currencySelect.Change("USD");
        await cut.InvokeAsync(() => setBudgetBtn.Click());

        // Assert monthly budget rendered
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId} .budget-amount-display").TextContent.Should().Contain("500.00"));

        // Assert monthly budget saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == groupId && b.PeriodType == BudgetPeriodType.Monthly);
            budget.Should().NotBeNull();
            budget!.Amount.Amount.Should().Be(500.00m);
            budget.Amount.Currency.Should().Be("USD");
        }

        // 4b. Set Group Weekly Budget
        var weeklyAmountInput = cut.Find($"#group-{groupId} .input-weekly-budget-amount");
        var weeklyCurrencySelect = cut.Find($"#group-{groupId} .select-weekly-budget-currency");
        var setWeeklyBudgetBtn = cut.Find($"#group-{groupId} .btn-set-weekly-budget");

        weeklyAmountInput.Change("150.00");
        weeklyCurrencySelect.Change("USD");
        await cut.InvokeAsync(() => setWeeklyBudgetBtn.Click());

        // Assert weekly budget rendered
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId} .weekly-budget-amount-display").TextContent.Should().Contain("150.00"));

        // Assert weekly budget saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == groupId && b.PeriodType == BudgetPeriodType.Weekly);
            budget.Should().NotBeNull();
            budget!.Amount.Amount.Should().Be(150.00m);
            budget.Amount.Currency.Should().Be("USD");
        }

        // 5. Add Member to Group
        var memberInput = cut.Find($"#group-{groupId} .input-member-email");
        var addMemberBtn = cut.Find($"#group-{groupId} .btn-add-member");

        memberInput.Change("developer@example.com");
        await cut.InvokeAsync(() => addMemberBtn.Click());

        // Assert member rendered
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId} .members-list").TextContent.Should().Contain("developer@example.com"));

        // Assert member saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var members = await db.GroupMemberships.Where(m => m.GroupId == groupId).ToListAsync();
            members.Should().ContainSingle(m => m.CallerId.Value == "developer@example.com");
        }

        // 6. Rename Group
        var renameBtn = cut.Find($"#group-{groupId} .btn-rename-group");
        await cut.InvokeAsync(() => renameBtn.Click());

        var nameInput = cut.Find($"#group-{groupId} .edit-group-name-input");
        var saveRenameBtn = cut.Find($"#group-{groupId} .btn-save-rename");

        nameInput.Change("engineering-team");
        await cut.InvokeAsync(() => saveRenameBtn.Click());

        // Assert rename rendered
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("engineering-team"));

        // Assert rename saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var group = await db.Groups.FindAsync(groupId);
            group!.Name.Should().Be("engineering-team");
        }

        // 7. Remove Member
        var removeMemberBtn = cut.Find($"#group-{groupId} .btn-remove-member");
        await cut.InvokeAsync(() => removeMemberBtn.Click());

        // Assert member removed visually
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId}").TextContent.Should().Contain("No members assigned to this group."));

        // Assert member removed in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyMember = await db.GroupMemberships.AnyAsync(m => m.GroupId == groupId);
            anyMember.Should().BeFalse();
        }

        // 8. Clear Budgets
        var clearBudgetBtn = cut.Find($"#group-{groupId} .btn-clear-budget");
        await cut.InvokeAsync(() => clearBudgetBtn.Click());

        var clearWeeklyBudgetBtn = cut.Find($"#group-{groupId} .btn-clear-weekly-budget");
        await cut.InvokeAsync(() => clearWeeklyBudgetBtn.Click());

        // Assert budget cleared visually
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId} .budget-amount-display").TextContent.Should().Contain("No budget set for current period."));
        cut.WaitForAssertion(() => cut.Find($"#group-{groupId} .weekly-budget-amount-display").TextContent.Should().Contain("No budget set for current period."));

        // Assert budget cleared in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyBudget = await db.GroupBudgets.AnyAsync(b => b.GroupId == groupId);
            anyBudget.Should().BeFalse();
        }

        // 9. Delete Group
        var deleteGroupBtn = cut.Find($"#group-{groupId} .btn-delete-group");
        await cut.InvokeAsync(() => deleteGroupBtn.Click());

        // Assert group deleted visually
        cut.WaitForAssertion(() => cut.FindAll($"#group-{groupId}").Should().BeEmpty());

        // Assert group deleted in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyGroup = await db.Groups.AnyAsync(g => g.Id == groupId);
            anyGroup.Should().BeFalse();
        }
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
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // Assert
        cut.WaitForAssertion(() =>
        {
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
        });
    }

    [Fact]
    public async Task AdminUser_BudgetValidation_RejectsNegative_AcceptsZero()
    {
        // Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // Seed a group in DB
        var group = Group.Create("dev");
        using (var db = _dbFactory.CreateDbContext())
        {
            await db.Groups.AddAsync(group);
            await db.SaveChangesAsync();
        }

        var cut = RenderComponent<Groups>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // 1. Try to set negative budget (-50.00)
        var amountInput = cut.WaitForElement($"#group-{group.Id} .input-budget-amount");
        var setBudgetBtn = cut.Find($"#group-{group.Id} .btn-set-budget");

        amountInput.Change("-50.00");
        await cut.InvokeAsync(() => setBudgetBtn.Click());

        // Assert error message rendered
        cut.WaitForAssertion(() =>
        {
            var errorBlock = cut.Find($"#group-{group.Id} .error-message-block");
            errorBlock.TextContent.Should().Contain("Monthly budget amount must be zero or positive.");
        });

        // Assert monthly budget was NOT saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == group.Id && b.PeriodType == BudgetPeriodType.Monthly);
            budget.Should().BeNull();
        }

        // 2. Set zero budget (0.00)
        var amountInput2 = cut.Find($"#group-{group.Id} .input-budget-amount");
        var setBudgetBtn2 = cut.Find($"#group-{group.Id} .btn-set-budget");
        amountInput2.Change("0.00");
        await cut.InvokeAsync(() => setBudgetBtn2.Click());

        // Assert monthly budget rendered as zero
        cut.WaitForAssertion(() => cut.Find($"#group-{group.Id} .budget-amount-display").TextContent.Should().Contain("0.00"));

        // Assert monthly budget saved as zero in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == group.Id && b.PeriodType == BudgetPeriodType.Monthly);
            budget.Should().NotBeNull();
            budget!.Amount.Amount.Should().Be(0.00m);
        }

        // 3. Try to set negative weekly budget (-25.00)
        var weeklyAmountInput = cut.WaitForElement($"#group-{group.Id} .input-weekly-budget-amount");
        var setWeeklyBudgetBtn = cut.Find($"#group-{group.Id} .btn-set-weekly-budget");

        weeklyAmountInput.Change("-25.00");
        await cut.InvokeAsync(() => setWeeklyBudgetBtn.Click());

        // Assert error message rendered for weekly
        cut.WaitForAssertion(() =>
        {
            var errorBlock = cut.Find($"#group-{group.Id} .error-message-block");
            errorBlock.TextContent.Should().Contain("Weekly budget amount must be zero or positive.");
        });

        // Assert weekly budget was NOT saved in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == group.Id && b.PeriodType == BudgetPeriodType.Weekly);
            budget.Should().BeNull();
        }

        // 4. Set zero weekly budget (0.00)
        var weeklyAmountInput2 = cut.Find($"#group-{group.Id} .input-weekly-budget-amount");
        var setWeeklyBudgetBtn2 = cut.Find($"#group-{group.Id} .btn-set-weekly-budget");
        weeklyAmountInput2.Change("0.00");
        await cut.InvokeAsync(() => setWeeklyBudgetBtn2.Click());

        // Assert weekly budget rendered as zero
        cut.WaitForAssertion(() => cut.Find($"#group-{group.Id} .weekly-budget-amount-display").TextContent.Should().Contain("0.00"));

        // Assert weekly budget saved as zero in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var budget = await db.GroupBudgets.FirstOrDefaultAsync(b => b.GroupId == group.Id && b.PeriodType == BudgetPeriodType.Weekly);
            budget.Should().NotBeNull();
            budget!.Amount.Amount.Should().Be(0.00m);
        }
    }
}
