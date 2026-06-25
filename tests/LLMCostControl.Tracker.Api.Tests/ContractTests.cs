using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Tracker.Api.Endpoints;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Contract / conformance tests for M19 (§13.4). These fix the gateway-facing
/// API shape — request and response JSON schemas for <c>POST /api/budget/check</c>
/// and <c>POST /api/usage/capture</c> and the error envelope — so that an
/// unintended breaking change fails CI.
/// <para>
/// A test here is a specification: if it breaks because of a code change, it
/// means the wire format has changed and callers (LLM gateways) must be
/// notified.
/// </para>
/// </summary>
public sealed class ContractTests : IClassFixture<TrackerApiFactory>
{
    private readonly TrackerApiFactory _factory;

    public ContractTests(TrackerApiFactory factory)
    {
        _factory = factory;
        factory.ResetStubs();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.JwtHelper.GenerateToken());
        return client;
    }

    // ── POST /api/budget/check — request schema ────────────────────────────────

    [Fact]
    public async Task Check_accepts_callerId_and_optional_model()
    {
        _factory.BudgetStore.SetBudget("schema-check@example.com",
            EffectiveBudget.FromUserOverride(new Money(50m, "USD")));

        var resp = await Client().PostAsJsonAsync("/api/budget/check", new
        {
            callerId = "schema-check@example.com",
            model = "gpt-4o",           // optional field
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Check_missing_callerId_returns_400()
    {
        var resp = await Client().PostAsJsonAsync("/api/budget/check", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/budget/check — response schema ───────────────────────────────

    [Fact]
    public async Task Check_response_has_required_fields()
    {
        _factory.BudgetStore.SetBudget("contract-check@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await Client().PostAsJsonAsync("/api/budget/check",
            new { callerId = "contract-check@example.com" });

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        body.TryGetProperty("allowed", out _).Should().BeTrue("'allowed' is required");
        body.TryGetProperty("callerId", out _).Should().BeTrue("'callerId' is required");
        body.TryGetProperty("runningSpend", out var rs).Should().BeTrue("'runningSpend' is required");
        rs.TryGetProperty("amount", out _).Should().BeTrue("runningSpend.amount is required");
        rs.TryGetProperty("currency", out _).Should().BeTrue("runningSpend.currency is required");
        body.TryGetProperty("remaining", out _).Should().BeTrue("'remaining' is required");
    }

    [Fact]
    public async Task Check_response_effectiveBudget_is_null_when_no_budget()
    {
        _factory.BudgetStore.SetBudget("no-budget-contract@example.com", EffectiveBudget.None());
        _factory.BudgetOptions.AllowNonBudgetedUsers = true;

        var body = JsonDocument.Parse(await (await Client().PostAsJsonAsync("/api/budget/check",
            new { callerId = "no-budget-contract@example.com" })).Content.ReadAsStringAsync()).RootElement;

        _factory.BudgetOptions.AllowNonBudgetedUsers = false;

        body.GetProperty("effectiveBudget").ValueKind.Should().Be(JsonValueKind.Null,
            "effectiveBudget must be null when caller has no budget");
    }

    // ── POST /api/usage/capture — request schema ───────────────────────────────

    [Fact]
    public async Task Capture_accepts_full_token_fields()
    {
        _factory.BudgetStore.SetBudget("contract-capture@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));

        var resp = await Client().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "contract-capture@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 200, cacheWrite = 50 },
            requestId = "contract-req-001",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Capture_missing_callerId_returns_400_with_error_envelope()
    {
        var resp = await Client().PostAsJsonAsync("/api/usage/capture", new
        {
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        body.TryGetProperty("error", out _).Should().BeTrue("error envelope must have 'error' field");
        body.TryGetProperty("detail", out _).Should().BeTrue("error envelope must have 'detail' field");
    }

    // ── POST /api/usage/capture — response schema ──────────────────────────────

    [Fact]
    public async Task Capture_response_has_required_fields()
    {
        _factory.BudgetStore.SetBudget("contract-cap-resp@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));
        _factory.PricingStore.SetPricing("gpt-4o",
            ModelPricing.Create(Provider.OpenAI, "gpt-4o", TokenPrices.Create(2.5m, 10m, 1.25m)));

        var resp = await Client().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "contract-cap-resp@example.com",
            model = "gpt-4o",
            tokens = new { input = 1000, output = 500, cacheRead = 0, cacheWrite = 0 },
        });

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        body.TryGetProperty("callerId", out _).Should().BeTrue("'callerId' is required");
        body.TryGetProperty("cost", out var cost).Should().BeTrue("'cost' is required");
        cost.TryGetProperty("amount", out _).Should().BeTrue("cost.amount is required");
        cost.TryGetProperty("currency", out _).Should().BeTrue("cost.currency is required");
        body.TryGetProperty("runningSpend", out _).Should().BeTrue("'runningSpend' is required");
        body.TryGetProperty("remaining", out _).Should().BeTrue("'remaining' is required");
    }

    // ── Unknown model error envelope ───────────────────────────────────────────

    [Fact]
    public async Task Capture_unknown_model_returns_error_envelope_with_unknown_model_code()
    {
        _factory.BudgetStore.SetBudget("contract-unkn@example.com",
            EffectiveBudget.FromUserOverride(new Money(100m, "USD")));

        var resp = await Client().PostAsJsonAsync("/api/usage/capture", new
        {
            callerId = "contract-unkn@example.com",
            model = "nonexistent-model",
            tokens = new { input = 100, output = 50, cacheRead = 0, cacheWrite = 0 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("error").GetString().Should().Be("unknown_model",
            "the error code for unknown models must be 'unknown_model'");
    }

    // ── Auth contract ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_auth_token_returns_401_on_both_endpoints()
    {
        var client = _factory.CreateClient(); // no auth header

        (await client.PostAsJsonAsync("/api/budget/check", new { callerId = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/usage/capture",
            new { callerId = "x", model = "y", tokens = new { input = 0, output = 0, cacheRead = 0, cacheWrite = 0 } }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ContractApiFactory reuses TrackerApiFactory directly (no subclass needed).
