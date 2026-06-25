using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Contract / conformance tests (M19, §13.4) that pin the gateway-facing API
/// shape — the exact request/response JSON schemas for <c>/api/budget/check</c>
/// and <c>/api/usage/capture</c> and the error envelopes — by asserting on the
/// raw JSON, so any unintended breaking change (a renamed/removed field) fails CI.
/// </summary>
public sealed class ContractTests : IClassFixture<TrackerApiFactory>
{
    private readonly TrackerApiFactory _factory;

    public ContractTests(TrackerApiFactory factory)
    {
        _factory = factory;
        _factory.ResetStubs();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.JwtHelper.GenerateToken());
        return client;
    }

    private static void AssertMoneyShape(JsonElement money)
    {
        money.ValueKind.Should().Be(JsonValueKind.Object);
        money.TryGetProperty("amount", out var amount).Should().BeTrue();
        amount.ValueKind.Should().Be(JsonValueKind.Number);
        money.TryGetProperty("currency", out var currency).Should().BeTrue();
        currency.ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Check_response_matches_the_contract_schema()
    {
        _factory.BudgetStore.SetBudget("contract-check@example.com",
            EffectiveBudget.FromUserOverride(new Money(50m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "contract-check@example.com", model = "gpt-4o" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("allowed").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.GetProperty("callerId").ValueKind.Should().Be(JsonValueKind.String);
        AssertMoneyShape(root.GetProperty("effectiveBudget"));
        AssertMoneyShape(root.GetProperty("runningSpend"));
        AssertMoneyShape(root.GetProperty("remaining"));
    }

    [Fact]
    public async Task Check_denied_response_has_null_effective_budget()
    {
        _factory.BudgetStore.SetBudget("contract-none@example.com", EffectiveBudget.None());

        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check",
            new { callerId = "contract-none@example.com" });

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("allowed").GetBoolean().Should().BeFalse();
        root.GetProperty("effectiveBudget").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Capture_response_matches_the_contract_schema()
    {
        _factory.BudgetStore.SetBudget("contract-capture@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "contract-capture@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 200, cacheWrite = 0 },
            requestId = "contract-req-1",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("callerId").ValueKind.Should().Be(JsonValueKind.String);
        AssertMoneyShape(root.GetProperty("cost"));
        AssertMoneyShape(root.GetProperty("runningSpend"));
        AssertMoneyShape(root.GetProperty("remaining"));
    }

    [Fact]
    public async Task Unknown_model_capture_returns_the_error_envelope()
    {
        _factory.BudgetStore.SetBudget("contract-unknown@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await CreateClient().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "contract-unknown@example.com",
            model = "does-not-exist",
            tokens = new { input = 1, output = 1, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetString().Should().Be("unknown_model");
        root.GetProperty("detail").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Invalid_request_returns_the_error_envelope()
    {
        var resp = await CreateClient().PostAsJsonAsync("/api/budget/check", new { callerId = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        root_error_is(doc, "invalid_request");
    }

    private static void root_error_is(JsonDocument doc, string expected)
        => doc.RootElement.GetProperty("error").GetString().Should().Be(expected);
}
