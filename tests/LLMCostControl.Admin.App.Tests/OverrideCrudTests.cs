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
/// bUnit tests for the User Budget Overrides CRUD capabilities (§12.3, M17).
/// Exercises listing, setting, updating, and clearing overrides.
/// </summary>
public sealed class OverrideCrudTests : TestContext, IDisposable
{
    private readonly SqliteTestDbContextFactory _dbFactory;

    public OverrideCrudTests()
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
    public async Task AdminUser_CanPerformFullOverrideCrudWorkflow()
    {
        // 1. Arrange & Seed
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // 2. Render Page
        var cut = RenderComponent<Overrides>();

        // 3. Set Override
        cut.Find("#callerId").Change("user@example.com");
        cut.Find("#amount").Change("150.00");
        cut.Find("#currency").Change("USD");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert override created in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "user@example.com" && o.Amount.Amount == 150.00m);
        }

        // Assert rendered in table
        var tableRowId = "#override-user-at-example-dot-com";
        var row = cut.Find(tableRowId);
        row.Should().NotBeNull();
        cut.Find($"{tableRowId} .override-caller-email").TextContent.Should().Be("user@example.com");
        cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("150.00");

        // 4. Update Override (by clicking edit to populate form, modifying, and clicking set override again)
        var editBtn = cut.Find($"{tableRowId} .btn-edit-override");
        await cut.InvokeAsync(() => editBtn.Click());

        // Assert form inputs populated
        cut.Find("#callerId").GetAttribute("value").Should().Be("user@example.com");
        cut.Find("#amount").GetAttribute("value").Should().StartWith("150");

        // Modify amount
        cut.Find("#amount").Change("250.00");
        await cut.InvokeAsync(() => cut.Find("#btn-save-override").Click());

        // Assert override updated in DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var overrides = await db.UserBudgetOverrides.ToListAsync();
            overrides.Should().ContainSingle(o => o.CallerId.Value == "user@example.com" && o.Amount.Amount == 250.00m);
        }
        cut.Find($"{tableRowId} .override-amount-display").TextContent.Should().Contain("250.00");

        // 5. Clear Override
        var clearBtn = cut.Find($"{tableRowId} .btn-clear-override");
        await cut.InvokeAsync(() => clearBtn.Click());

        // Assert override removed from DB
        using (var db = _dbFactory.CreateDbContext())
        {
            var anyOverride = await db.UserBudgetOverrides.AnyAsync();
            anyOverride.Should().BeFalse();
        }
        cut.FindAll(tableRowId).Should().BeEmpty();
        cut.Markup.Should().Contain("No per-user overrides found for this period.");
    }

    [Fact]
    public async Task ReadOnlyUser_CannotPerformOverrideModifications()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("reader@example.com");
        authContext.SetRoles("CostTracker.ReadOnly");

        // Seed an override in DB
        var ov = UserBudgetOverride.Create(CallerId.From("user@example.com"), new Money(100m, "USD"), BudgetPeriod.Current());
        using (var db = _dbFactory.CreateDbContext())
        {
            await db.UserBudgetOverrides.AddAsync(ov);
            await db.SaveChangesAsync();
        }

        // Act
        var cut = RenderComponent<Overrides>();

        // Assert
        var tableRowId = "#override-user-at-example-dot-com";
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
    }
}
