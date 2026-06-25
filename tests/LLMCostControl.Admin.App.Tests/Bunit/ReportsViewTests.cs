using Bunit;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LLMCostControl.Admin.App.Tests.Bunit;

/// <summary>
/// bUnit tests for M18 read-only views (§12.3). Uses NSubstitute to mock
/// <see cref="IAdminReadService"/> so no database is required.
/// </summary>
public sealed class ReportsViewTests : BunitContext
{
    private readonly IAdminReadService _readService;

    public ReportsViewTests()
    {
        _readService = Substitute.For<IAdminReadService>();

        // Defaults: empty results
        _readService.GetCallerSummaryAsync(Arg.Any<string>(), Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<CallerBudgetSummary?>(null));
        _readService.GetRecentEventsAsync(Arg.Any<string>(), Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<UsageEvent>>([]));

        Services.AddSingleton(_readService);
        Services.AddAuthorizationCore();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Reports_page_renders_heading()
    {
        var cut = Render<Reports>();
        cut.Find("#reports-heading").TextContent.Should().Contain("Reports");
    }

    [Fact]
    public void Caller_summary_shows_effective_budget_and_spend()
    {
        var summary = new CallerBudgetSummary
        {
            EffectiveBudget = EffectiveBudget.FromGroup(new Money(100m, "USD"), Guid.NewGuid()),
            RunningSpend = 12.34m,
            Currency = "USD",
        };
        _readService.GetCallerSummaryAsync("test@example.com", Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<CallerBudgetSummary?>(summary));

        var cut = Render<Reports>();
        cut.Find("#caller-lookup-input").Change("test@example.com");
        cut.Find("#caller-lookup-btn").Click();

        cut.Find("#effective-budget").TextContent.Should().Contain("100");
        cut.Find("#running-spend").TextContent.Should().Contain("12.3400");
    }

    [Fact]
    public void Caller_with_no_events_shows_no_events_message()
    {
        _readService.GetCallerSummaryAsync("empty@example.com", Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<CallerBudgetSummary?>(new CallerBudgetSummary
                    {
                        EffectiveBudget = EffectiveBudget.None(),
                        RunningSpend = 0m,
                        Currency = "USD",
                    }));

        var cut = Render<Reports>();
        cut.Find("#caller-lookup-input").Change("empty@example.com");
        cut.Find("#caller-lookup-btn").Click();

        cut.Find("#no-events").TextContent.Should().Contain("No usage events");
    }
}
