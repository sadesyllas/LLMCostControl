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
/// bUnit tests for the User Budget Overrides CRUD capabilities (§12.3, M17).
/// Exercises listing, setting, updating, and clearing overrides.
/// </summary>
[Collection("PostgresCollection")]
public sealed class OverrideCrudTests : TestContext, IDisposable
{
    private readonly PostgresTestDbContextFactory _dbFactory;

    public OverrideCrudTests(PostgresFixture fixture)
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
    public async Task AdminUser_CanPerformFullOverrideCrudWorkflow()
    {
        // 1. Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // 2. Render Page & Wait for Load
        var cut = RenderComponent<Overrides>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // 3. Set Override
        cut.Find("#callerId").Change("user@example.com");
        cut.Find("#amount").Change("150.00");
        cut.Find("#currency").Change("USD");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert rendered in table first (await async reload)
        var tableRowId = "#override-user-at-example-dot-com-monthly";
        cut.WaitForAssertion(() =>
        {
            var row = cut.Find(tableRowId);
            row.Should().NotBeNull();
            cut.Find($"{tableRowId} .override-caller-email").TextContent.Should().Be("user@example.com");
            cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("150.00");
        });

        // Assert override created in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "user@example.com" && o.Amount.Amount == 150.00m);
        }

        // 4. Update Override (by clicking edit to populate form, modifying, and clicking set override again)
        var editBtn = cut.Find($"{tableRowId} .btn-edit-override");
        await cut.InvokeAsync(() => editBtn.Click());

        // Assert form inputs populated
        cut.Find("#callerId").GetAttribute("value").Should().Be("user@example.com");
        cut.Find("#amount").GetAttribute("value").Should().StartWith("150");

        // Modify amount
        cut.Find("#amount").Change("250.00");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert updated visually first (await async reload)
        cut.WaitForAssertion(() =>
        {
            cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("250.00");
        });

        // Assert override updated in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "user@example.com" && o.Amount.Amount == 250.00m);
        }

        // 5. Clear Override
        var clearBtn = cut.Find($"{tableRowId} .btn-clear-override");
        await cut.InvokeAsync(() => clearBtn.Click());

        // Assert override removed visually first (await async reload)
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(tableRowId).Should().BeEmpty();
            cut.Markup.Should().Contain("No per-user overrides found.");
        });

        // Assert override removed from DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyOverride = await db.UserBudgetOverrides.AnyAsync();
            anyOverride.Should().BeFalse();
        }
    }

    [Fact]
    public async Task AdminUser_CanPerformWeeklyOverrideCrudWorkflow()
    {
        // 1. Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // 2. Render Page & Wait for Load
        var cut = RenderComponent<Overrides>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // 3. Set Weekly Override
        cut.Find("#callerId").Change("weekly-user@example.com");
        cut.Find("#periodType").Change(BudgetPeriodType.Weekly.ToString());
        cut.Find("#amount").Change("75.00");
        cut.Find("#currency").Change("USD");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert rendered in table first (await async reload)
        var tableRowId = "#override-weekly-user-at-example-dot-com-weekly";
        cut.WaitForAssertion(() =>
        {
            var row = cut.Find(tableRowId);
            row.Should().NotBeNull();
            cut.Find($"{tableRowId} .override-caller-email").TextContent.Should().Be("weekly-user@example.com");
            cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("75.00");
        });

        // Assert override created in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "weekly-user@example.com" && o.Amount.Amount == 75.00m && o.PeriodType == BudgetPeriodType.Weekly);
        }

        // 4. Update Override
        var editBtn = cut.Find($"{tableRowId} .btn-edit-override");
        await cut.InvokeAsync(() => editBtn.Click());

        // Assert form inputs populated
        cut.Find("#callerId").GetAttribute("value").Should().Be("weekly-user@example.com");
        cut.Find("#amount").GetAttribute("value").Should().StartWith("75");

        // Modify amount
        cut.Find("#amount").Change("120.00");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert updated visually first (await async reload)
        cut.WaitForAssertion(() =>
        {
            cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("120.00");
        });

        // Assert override updated in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "weekly-user@example.com" && o.Amount.Amount == 120.00m && o.PeriodType == BudgetPeriodType.Weekly);
        }

        // 5. Clear Override
        var clearBtn = cut.Find($"{tableRowId} .btn-clear-override");
        await cut.InvokeAsync(() => clearBtn.Click());

        // Assert override removed visually first (await async reload)
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(tableRowId).Should().BeEmpty();
        });

        // Assert override removed from DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyOverride = await db.UserBudgetOverrides.AnyAsync();
            anyOverride.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ReadOnlyUser_CannotPerformOverrideModifications()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("reader@example.com");
        authContext.SetRoles("CostTracker.ReadOnly");

        // Seed an override in DB
        var ov = UserBudgetOverride.Create(CallerId.From("user@example.com"), new Money(100m, "USD"), BudgetPeriodType.Monthly);
        using (var db = _dbFactory.CreateDbContext())
        {
            await db.UserBudgetOverrides.AddAsync(ov);
            await db.SaveChangesAsync();
        }

        // Act
        var cut = RenderComponent<Overrides>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // Assert
        var tableRowId = "#override-user-at-example-dot-com-monthly";
        cut.WaitForAssertion(() =>
        {
            cut.Find(tableRowId).Should().NotBeNull();
            cut.Find($"{tableRowId} .override-caller-email").TextContent.Should().Be("user@example.com");

            // Form elements for setting overrides should be hidden
            cut.FindAll("#callerId").Should().BeEmpty();
            cut.FindAll("#amount").Should().BeEmpty();
            cut.FindAll("#currency").Should().BeEmpty();
            cut.FindAll("#btn-save-override").Should().BeEmpty();

            // Row edit and clear buttons should be hidden
            cut.FindAll($"{tableRowId} .btn-edit-override").Should().BeEmpty();
            cut.FindAll($"{tableRowId} .btn-clear-override").Should().BeEmpty();
        });
    }
}
