using System;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Usage;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the Callers & Spend monitoring view (§12.3, M18).
/// </summary>
[Collection("PostgresCollection")]
public sealed class CallersViewTests : TestContext, IDisposable
{
    private readonly PostgresTestDbContextFactory _dbFactory;
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="CallersViewTests"/> class.
    /// </summary>
    public CallersViewTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        _dbFactory = new PostgresTestDbContextFactory(fixture.ConnectionString);
        _dbFactory.ResetDatabase();
        Services.AddSingleton<IDbContextFactory<CostTrackerDbContext>>(_dbFactory);
    }

    /// <summary>Disposes of the test services.</summary>
    public new void Dispose()
    {
        base.Dispose();
    }

    /// <summary>
    /// Verifies that when no callers or budget data exist in the system, a proper empty state message is shown.
    /// </summary>
    [Fact]
    public void CallersView_EmptyState_ShowsNoCallersFound()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // Act
        var cut = RenderComponent<Callers>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No active callers or budget allocations found in the system.");
        });
    }

    /// <summary>
    /// Verifies that when active callers are seeded in the database, the Callers list renders correctly,
    /// effective budgets are resolved, and clicking a caller displays their detailed usage audit trail.
    /// </summary>
    [Fact]
    public async Task CallersView_WithSeededData_RendersBudgetSpendAndDetails()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        var period = BudgetPeriod.Current();
        var caller = CallerId.From("bob@example.com");

        var smallGroup = Group.Create("Small Group");
        var bigGroup = Group.Create("Big Group");

        using (var db = _dbFactory.CreateDbContext())
        {
            var groupRepo = new GroupRepository(db);
            await groupRepo.AddAsync(smallGroup);
            await groupRepo.AddAsync(bigGroup);

            var membershipRepo = new GroupMembershipRepository(db);
            await membershipRepo.AddAsync(GroupMembership.Create(smallGroup.Id, caller));
            await membershipRepo.AddAsync(GroupMembership.Create(bigGroup.Id, caller));

            var budgetRepo = new GroupBudgetRepository(db);
            await budgetRepo.UpsertAsync(GroupBudget.Create(smallGroup.Id, new Money(30m, "USD"), period));
            await budgetRepo.UpsertAsync(GroupBudget.Create(bigGroup.Id, new Money(75m, "USD"), period));

            var usageEventRepo = new UsageEventRepository(db);
            var ev1 = UsageEvent.Create(
                eventId: "event-1",
                callerId: caller,
                effectiveGroupId: bigGroup.Id,
                budgetSource: BudgetSource.Group,
                model: "gpt-4o",
                provider: Provider.OpenAI,
                tokensInput: 1000,
                tokensOutput: 500,
                tokensCacheRead: 0,
                tokensCacheWrite: 0,
                unitPrices: TokenPrices.Create(5m, 15m, 0m),
                costAmount: 0.0125m,
                costCurrency: "USD",
                runningSpendAfter: 0.0125m,
                period: period,
                capturedAt: DateTimeOffset.UtcNow
            );
            await usageEventRepo.AppendAsync(ev1);
        }

        // Act
        var cut = RenderComponent<Callers>();
        cut.WaitForAssertion(() => cut.FindAll(".spinner-border").Should().BeEmpty());

        // Assert table renders caller with effective budget (largest: 75.00 USD), spend, and source.
        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("#callers-table");
            table.Should().NotBeNull();
        });

        var rowId = "#caller-row-bob-at-example-dot-com";
        var row = cut.Find(rowId);
        row.Should().NotBeNull();
        row.TextContent.Should().Contain("bob@example.com");
        row.TextContent.Should().Contain("75.00 USD");
        row.TextContent.Should().Contain("Group");

        // Select the caller to display audit trail
        await cut.InvokeAsync(() => row.Click());

        // Assert audit trail displays the usage event details
        cut.WaitForAssertion(() =>
        {
            var auditTrail = cut.Find("#audit-trail-container");
            auditTrail.Should().NotBeNull();
            auditTrail.TextContent.Should().Contain("gpt-4o");
            auditTrail.TextContent.Should().Contain("0.0125 USD");
            auditTrail.TextContent.Should().Contain("Input");
            auditTrail.TextContent.Should().Contain("1000");
        });
    }
}
