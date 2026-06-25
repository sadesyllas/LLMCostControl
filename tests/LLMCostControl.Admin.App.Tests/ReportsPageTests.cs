using Bunit;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using Microsoft.Extensions.DependencyInjection;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the read-only Reports view (M18, §12.3): caller budget/spend
/// rendering and a staleness badge on stale pricing.
/// </summary>
public sealed class ReportsPageTests : BunitContext
{
    [Fact]
    public void Pricing_table_renders_a_staleness_badge_for_stale_pricing()
    {
        var query = new FakeAdminQueryService();
        var fresh = ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m));
        var stale = ModelPricing.Create(Provider.Anthropic, "claude-3-5-sonnet", TokenPrices.Create(3m, 15m, 0.3m));
        stale.StaleSince = DateTimeOffset.UtcNow;
        query.Pricing.Add(fresh);
        query.Pricing.Add(stale);
        Services.AddSingleton<IAdminQueryService>(query);

        var cut = Render<Reports>();

        cut.FindAll("[data-testid=pricing-row]").Should().HaveCount(2);
        cut.FindAll("[data-testid=stale-badge]").Should().ContainSingle();
        cut.FindAll("[data-testid=fresh-badge]").Should().ContainSingle();
    }

    [Fact]
    public void Caller_lookup_renders_effective_budget_spend_and_remaining()
    {
        var query = new FakeAdminQueryService
        {
            Summary = new CallerBudgetSummary(
                "alice@example.com",
                EffectiveBudget.FromGroup(new Money(250m, "USD"), Guid.NewGuid()),
                new Money(40m, "USD"),
                new Money(210m, "USD")),
        };
        Services.AddSingleton<IAdminQueryService>(query);

        var cut = Render<Reports>();
        cut.Find("[data-testid=caller-input]").Change("alice@example.com");
        cut.Find("[data-testid=lookup-caller]").Click();

        cut.Find("[data-testid=budget-source]").TextContent.Should().Be("Group");
        cut.Find("[data-testid=effective-budget]").TextContent.Should().Contain("250");
        cut.Find("[data-testid=running-spend]").TextContent.Should().Contain("40");
        cut.Find("[data-testid=remaining]").TextContent.Should().Contain("210");
    }
}
