using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Conformance and contract tests that fix the gateway-facing API shape (§13.4, M19).
/// Verifies property naming, casing, and nested structures for check, capture, and error endpoints.
/// </summary>
public sealed class ContractTests : IClassFixture<TrackerApiFactory>
{
    private readonly TrackerApiFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractTests"/> class.
    /// </summary>
    public ContractTests(TrackerApiFactory factory)
    {
        _factory = factory;
        _factory.ResetStubs();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        var token = _factory.JwtHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void SeedPricing(string model, decimal input, decimal output, decimal? cacheRead = null)
    {
        _factory.PricingStore.SetPricing(
            ModelPricing.Create(Provider.OpenAI, model,
                TokenPrices.Create(input, output, cacheRead)));
    }

    private void SeedBudget(string callerId, EffectiveBudget budget)
    {
        _factory.BudgetStore.SetBudget(callerId, budget);
    }

    /// <summary>
    /// Verifies the JSON contract of the budget check response, asserting that all required property names
    /// exist and are serialized in camelCase.
    /// </summary>
    [Fact]
    public async Task BudgetCheckResponse_ConformsToContractSchema()
    {
        // Arrange
        var callerId = "contract-check@example.com";
        _factory.BudgetStore.SetBudget(callerId, BudgetPeriodType.Monthly, EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.BudgetStore.SetBudget(callerId, BudgetPeriodType.Weekly, EffectiveBudget.FromUserOverride(new Money(50m, "USD")));

        // Act
        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonString = await resp.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(jsonString)?.AsObject();

        // Assert top-level property names and types
        root.Should().NotBeNull();
        root!.ContainsKey("allowed").Should().BeTrue();
        root["allowed"]!.GetValue<bool>().Should().BeTrue();

        root.ContainsKey("callerId").Should().BeTrue();
        root["callerId"]!.GetValue<string>().Should().Be(callerId);

        root.ContainsKey("budgets").Should().BeTrue();
        var budgetsArray = root["budgets"]?.AsArray();
        budgetsArray.Should().NotBeNull();
        budgetsArray.Should().HaveCount(2);

        var monthly = budgetsArray!.Select(x => x!.AsObject()).First(x => x["period"]!.GetValue<string>() == "Monthly");
        monthly.ContainsKey("period").Should().BeTrue();
        monthly.ContainsKey("periodKey").Should().BeTrue();
        monthly.ContainsKey("budgetSource").Should().BeTrue();
        monthly.ContainsKey("effectiveBudget").Should().BeTrue();
        
        var budgetNode = monthly["effectiveBudget"]?.AsObject();
        budgetNode.Should().NotBeNull();
        budgetNode!.ContainsKey("amount").Should().BeTrue();
        budgetNode["amount"]!.GetValue<decimal>().Should().Be(100m);
        budgetNode.ContainsKey("currency").Should().BeTrue();
        budgetNode["currency"]!.GetValue<string>().Should().Be("USD");

        monthly.ContainsKey("runningSpend").Should().BeTrue();
        var spendNode = monthly["runningSpend"]?.AsObject();
        spendNode.Should().NotBeNull();
        spendNode!.ContainsKey("amount").Should().BeTrue();
        spendNode.ContainsKey("currency").Should().BeTrue();

        monthly.ContainsKey("remaining").Should().BeTrue();
        var remainingNode = monthly["remaining"]?.AsObject();
        remainingNode.Should().NotBeNull();
        remainingNode!.ContainsKey("amount").Should().BeTrue();
        remainingNode["amount"]!.GetValue<decimal>().Should().Be(100m);
        remainingNode.ContainsKey("currency").Should().BeTrue();

        // Weekly contract verification
        var weekly = budgetsArray!.Select(x => x!.AsObject()).First(x => x["period"]!.GetValue<string>() == "Weekly");
        weekly.ContainsKey("period").Should().BeTrue();
        weekly.ContainsKey("periodKey").Should().BeTrue();
        weekly.ContainsKey("budgetSource").Should().BeTrue();
        weekly.ContainsKey("effectiveBudget").Should().BeTrue();
        
        var weeklyBudgetNode = weekly["effectiveBudget"]?.AsObject();
        weeklyBudgetNode.Should().NotBeNull();
        weeklyBudgetNode!.ContainsKey("amount").Should().BeTrue();
        weeklyBudgetNode["amount"]!.GetValue<decimal>().Should().Be(50m);
        weeklyBudgetNode.ContainsKey("currency").Should().BeTrue();
        weeklyBudgetNode["currency"]!.GetValue<string>().Should().Be("USD");

        weekly.ContainsKey("runningSpend").Should().BeTrue();
        var weeklySpendNode = weekly["runningSpend"]?.AsObject();
        weeklySpendNode.Should().NotBeNull();
        weeklySpendNode!.ContainsKey("amount").Should().BeTrue();
        weeklySpendNode.ContainsKey("currency").Should().BeTrue();

        weekly.ContainsKey("remaining").Should().BeTrue();
        var weeklyRemainingNode = weekly["remaining"]?.AsObject();
        weeklyRemainingNode.Should().NotBeNull();
        weeklyRemainingNode!.ContainsKey("amount").Should().BeTrue();
        weeklyRemainingNode["amount"]!.GetValue<decimal>().Should().Be(50m);
        weeklyRemainingNode.ContainsKey("currency").Should().BeTrue();
    }

    /// <summary>
    /// Verifies the JSON contract of the usage capture response, asserting that all required property names
    /// exist and are serialized in camelCase.
    /// </summary>
    [Fact]
    public async Task UsageCaptureResponse_ConformsToContractSchema()
    {
        // Arrange
        var callerId = "contract-capture@example.com";
        _factory.BudgetStore.SetBudget(callerId, BudgetPeriodType.Monthly, EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.BudgetStore.SetBudget(callerId, BudgetPeriodType.Weekly, EffectiveBudget.FromUserOverride(new Money(50m, "USD")));
        SeedPricing("gpt-4o", 2.5m, 10m);

        var captureRequest = new
        {
            callerId,
            model = "gpt-4o",
            tokens = new
            {
                input = 1000,
                output = 500,
                cacheRead = 0,
                cacheWrite = 0
            },
            requestId = Guid.NewGuid().ToString()
        };

        // Act
        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", captureRequest);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonString = await resp.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(jsonString)?.AsObject();

        // Assert properties in capture response
        root.Should().NotBeNull();
        root!.ContainsKey("callerId").Should().BeTrue();
        root["callerId"]!.GetValue<string>().Should().Be(callerId);

        root.ContainsKey("cost").Should().BeTrue();
        var costNode = root["cost"]?.AsObject();
        costNode.Should().NotBeNull();
        costNode!.ContainsKey("amount").Should().BeTrue();
        costNode["amount"]!.GetValue<decimal>().Should().Be(0.0075m); // (1000 * 2.5 / 1M) + (500 * 10 / 1M) = 0.0025 + 0.0050 = 0.0075m
        costNode.ContainsKey("currency").Should().BeTrue();
        costNode["currency"]!.GetValue<string>().Should().Be("USD");

        root.ContainsKey("budgets").Should().BeTrue();
        var budgetsArray = root["budgets"]?.AsArray();
        budgetsArray.Should().NotBeNull();
        budgetsArray.Should().HaveCount(2);

        var monthly = budgetsArray!.Select(x => x!.AsObject()).First(x => x["period"]!.GetValue<string>() == "Monthly");
        monthly.ContainsKey("period").Should().BeTrue();
        monthly.ContainsKey("periodKey").Should().BeTrue();

        monthly.ContainsKey("runningSpend").Should().BeTrue();
        var spendNode = monthly["runningSpend"]?.AsObject();
        spendNode.Should().NotBeNull();
        spendNode!.ContainsKey("amount").Should().BeTrue();
        spendNode["amount"]!.GetValue<decimal>().Should().Be(0.0075m);

        monthly.ContainsKey("remaining").Should().BeTrue();
        var remainingNode = monthly["remaining"]?.AsObject();
        remainingNode.Should().NotBeNull();
        remainingNode!.ContainsKey("amount").Should().BeTrue();
        remainingNode["amount"]!.GetValue<decimal>().Should().Be(100m - 0.0075m);

        var weekly = budgetsArray!.Select(x => x!.AsObject()).First(x => x["period"]!.GetValue<string>() == "Weekly");
        weekly.ContainsKey("period").Should().BeTrue();
        weekly.ContainsKey("periodKey").Should().BeTrue();

        weekly.ContainsKey("runningSpend").Should().BeTrue();
        var weeklySpendNode = weekly["runningSpend"]?.AsObject();
        weeklySpendNode.Should().NotBeNull();
        weeklySpendNode!.ContainsKey("amount").Should().BeTrue();
        weeklySpendNode["amount"]!.GetValue<decimal>().Should().Be(0.0075m);

        weekly.ContainsKey("remaining").Should().BeTrue();
        var weeklyRemainingNode = weekly["remaining"]?.AsObject();
        weeklyRemainingNode.Should().NotBeNull();
        weeklyRemainingNode!.ContainsKey("amount").Should().BeTrue();
        weeklyRemainingNode["amount"]!.GetValue<decimal>().Should().Be(50m - 0.0075m);
    }

    /// <summary>
    /// Verifies the JSON contract of the error response envelope, asserting that it returns
    /// the expected camelCase error structures.
    /// </summary>
    [Fact]
    public async Task ErrorResponse_ConformsToContractSchema()
    {
        // Act: capture on a model that hasn't been seeded (unknown model)
        var captureRequest = new
        {
            callerId = "contract-error@example.com",
            model = "unknown-nonexistent-model",
            tokens = new
            {
                input = 100,
                output = 100
            }
        };

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", captureRequest);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var jsonString = await resp.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(jsonString)?.AsObject();

        // Assert error schema properties
        root.Should().NotBeNull();
        root!.ContainsKey("error").Should().BeTrue();
        root["error"]!.GetValue<string>().Should().Be("unknown_model");
        root.ContainsKey("detail").Should().BeTrue();
    }
}
