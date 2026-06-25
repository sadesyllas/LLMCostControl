using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

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
        _factory.PricingStore.SetPricing(model,
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
        SeedBudget("contract-check@example.com", EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        // Act
        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "contract-check@example.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonString = await resp.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(jsonString)?.AsObject();

        // Assert top-level property names and types
        root.Should().NotBeNull();
        root!.ContainsKey("allowed").Should().BeTrue();
        root["allowed"]!.GetValue<bool>().Should().BeTrue();

        root.ContainsKey("callerId").Should().BeTrue();
        root["callerId"]!.GetValue<string>().Should().Be("contract-check@example.com");

        root.ContainsKey("effectiveBudget").Should().BeTrue();
        var budgetNode = root["effectiveBudget"]?.AsObject();
        budgetNode.Should().NotBeNull();
        budgetNode!.ContainsKey("amount").Should().BeTrue();
        budgetNode["amount"]!.GetValue<decimal>().Should().Be(100m);
        budgetNode.ContainsKey("currency").Should().BeTrue();
        budgetNode["currency"]!.GetValue<string>().Should().Be("USD");

        root.ContainsKey("runningSpend").Should().BeTrue();
        var spendNode = root["runningSpend"]?.AsObject();
        spendNode.Should().NotBeNull();
        spendNode!.ContainsKey("amount").Should().BeTrue();
        spendNode.ContainsKey("currency").Should().BeTrue();

        root.ContainsKey("remaining").Should().BeTrue();
        var remainingNode = root["remaining"]?.AsObject();
        remainingNode.Should().NotBeNull();
        remainingNode!.ContainsKey("amount").Should().BeTrue();
        remainingNode["amount"]!.GetValue<decimal>().Should().Be(100m);
        remainingNode.ContainsKey("currency").Should().BeTrue();
    }

    /// <summary>
    /// Verifies the JSON contract of the usage capture response, asserting that all required property names
    /// exist and are serialized in camelCase.
    /// </summary>
    [Fact]
    public async Task UsageCaptureResponse_ConformsToContractSchema()
    {
        // Arrange
        SeedBudget("contract-capture@example.com", EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        SeedPricing("gpt-4o", 2.5m, 10m);

        var captureRequest = new
        {
            callerId = "contract-capture@example.com",
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
        root["callerId"]!.GetValue<string>().Should().Be("contract-capture@example.com");

        root.ContainsKey("cost").Should().BeTrue();
        var costNode = root["cost"]?.AsObject();
        costNode.Should().NotBeNull();
        costNode!.ContainsKey("amount").Should().BeTrue();
        costNode["amount"]!.GetValue<decimal>().Should().Be(0.0075m); // (1000 * 2.5 / 1M) + (500 * 10 / 1M) = 0.0025 + 0.0050 = 0.0075m
        costNode.ContainsKey("currency").Should().BeTrue();
        costNode["currency"]!.GetValue<string>().Should().Be("USD");

        root.ContainsKey("runningSpend").Should().BeTrue();
        var spendNode = root["runningSpend"]?.AsObject();
        spendNode.Should().NotBeNull();
        spendNode!.ContainsKey("amount").Should().BeTrue();
        spendNode["amount"]!.GetValue<decimal>().Should().Be(0.0075m);

        root.ContainsKey("remaining").Should().BeTrue();
        var remainingNode = root["remaining"]?.AsObject();
        remainingNode.Should().NotBeNull();
        remainingNode!.ContainsKey("amount").Should().BeTrue();
        remainingNode["amount"]!.GetValue<decimal>().Should().Be(100m - 0.0075m);
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
